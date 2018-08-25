using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v2;
using Google.Apis.Drive.v2.Data;
using Google.Apis.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Querying;
using File = Google.Apis.Drive.v2.Data.File;

namespace MediaBrowser.Plugins.GoogleDrive
{
    public class GoogleDriveService
    {
        private const string SyncFolderPropertyKey = "CloudSyncFolder";
        private const string SyncFolderPropertyValue = "ba460da6-2cdf-43d8-98fc-ecda617ff1db";

        public async Task<QueryResult<FileSystemMetadata>> GetFiles(string[] pathParts, string rootFolderId, GoogleCredentials googleCredentials,
         CancellationToken cancellationToken)
        {
            var fullDriveService = CreateDriveServiceAndCredentials(googleCredentials);
            var driveService = fullDriveService.Item1;

            var result = new QueryResult<FileSystemMetadata>();

            if (pathParts != null && pathParts.Length > 0)
            {
                try
                {
                    var parentId = await GetFolderFromPath(driveService, pathParts, rootFolderId, false, cancellationToken).ConfigureAwait(false);

                    if (!string.IsNullOrEmpty(parentId))
                    {
                        result = await this.GetFilesInFolder(parentId, googleCredentials, driveService, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (FileNotFoundException)
                {

                }

                return result;
            }

            return result;
        }

        private FileSystemMetadata GetFileMetadata(File file)
        {
            return new FileSystemMetadata
            {
                IsDirectory = string.Equals(file.MimeType, "application/vnd.google-apps.folder", StringComparison.OrdinalIgnoreCase),
                Name = file.Title,
                FullName = file.Id,
                //MimeType = file.MimeType
            };
        }

        private async Task<string> GetFolderFromPath(DriveService driveService, string[] pathParts, string rootParentId, bool createIfNotExists, CancellationToken cancellationToken)
        {
            string currentparentId = rootParentId;

            foreach (var part in pathParts)
            {
                currentparentId = await GetChildFolder(part, currentparentId, driveService, createIfNotExists, cancellationToken).ConfigureAwait(false);

                if (currentparentId == null)
                {
                    return null;
                }
            }

            return currentparentId;
        }

        public async Task<Tuple<string,string>> UploadFile(Stream stream, string[] pathParts, string folderId, GoogleCredentials googleCredentials, IProgress<double> progress, CancellationToken cancellationToken)
        {
            var name = pathParts.Last();
            pathParts = pathParts.Take(pathParts.Length - 1).ToArray();

            var fullDriveService = CreateDriveServiceAndCredentials(googleCredentials);
            var driveService = fullDriveService.Item1;

            var parentId = await GetFolderFromPath(driveService, pathParts, folderId, true, cancellationToken).ConfigureAwait(false);
            await TryDeleteFile(parentId, name, driveService, cancellationToken).ConfigureAwait(false);

            var googleDriveFile = CreateGoogleDriveFile(pathParts, name, folderId);
            googleDriveFile.GoogleDriveFolderId = parentId;

            var file = CreateFileToUpload(googleDriveFile);
            await ExecuteUpload(driveService, stream, file, progress, cancellationToken).ConfigureAwait(false);

            var uploadedFile = await FindFileId(name, parentId, driveService, cancellationToken).ConfigureAwait(false);
            return new Tuple<string, string>(uploadedFile.Id, uploadedFile.DownloadUrl + "&access_token=" + fullDriveService.Item2.Token.AccessToken);
        }

        private GoogleDriveFile CreateGoogleDriveFile(string[] pathParts, string name, string folderId)
        {
            var folder = Path.Combine(pathParts);

            return new GoogleDriveFile
            {
                Name = name,
                FolderPath = folder,
                GoogleDriveFolderId = folderId
            };
        }

        public async Task<string> CreateDownloadUrl(string fileId, GoogleCredentials googleCredentials, CancellationToken cancellationToken)
        {
            var fullDriveService = CreateDriveServiceAndCredentials(googleCredentials);
            var driveService = fullDriveService.Item1;

            var uploadedFile = await GetFile(fileId, driveService, cancellationToken).ConfigureAwait(false);
            return uploadedFile.DownloadUrl + "&access_token=" + fullDriveService.Item2.Token.AccessToken;
        }

        public Task<string> GetChildFolder(string name, string parentId, GoogleCredentials googleCredentials, bool createIfNotExists, CancellationToken cancellationToken)
        {
            var driveService = CreateDriveServiceAndCredentials(googleCredentials).Item1;
            return GetChildFolder(name, parentId, driveService, createIfNotExists, cancellationToken);
        }

        public async Task<string> GetChildFolder(string name, string parentId, DriveService driveService, bool createIfNotExists, CancellationToken cancellationToken)
        {
            var folder = await FindFolder(name, parentId, driveService, cancellationToken).ConfigureAwait(false);

            if (folder != null)
            {
                return folder.Id;
            }

            if (createIfNotExists)
            {
                return await CreateFolder(name, parentId, cancellationToken, driveService).ConfigureAwait(false);
            }

            return null;
        }

        private async Task TryDeleteFile(string parentFolderId, string name, DriveService driveService, CancellationToken cancellationToken)
        {
            try
            {
                var file = await FindFileId(name, parentFolderId, driveService, cancellationToken).ConfigureAwait(false);

                var request = driveService.Files.Delete(file.Id);
                await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (FileNotFoundException) { }
        }

        public async Task DeleteFile(string fileId, GoogleCredentials googleCredentials, CancellationToken cancellationToken)
        {
            var fullDriveService = CreateDriveServiceAndCredentials(googleCredentials);
            var driveService = fullDriveService.Item1;

            var file = await GetFile(fileId, driveService, cancellationToken).ConfigureAwait(false);

            var request = driveService.Files.Delete(file.Id);
            await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task<File> GetFile(string fileId, DriveService driveService, CancellationToken cancellationToken)
        {
            var request = driveService.Files.Get(fileId);
            return request.ExecuteAsync(cancellationToken);
        }

        public async Task<File> FindFileId(string name, string parentFolderId, DriveService driveService, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException("name");
            }
            if (string.IsNullOrWhiteSpace(parentFolderId))
            {
                throw new ArgumentNullException("parentFolderId");
            }
            var queryName = name.Replace("'", "\\'");
            var query = string.Format("title = '{0}'", queryName);

            query += string.Format(" and '{0}' in parents", parentFolderId);

            var matchingFiles = await GetFiles(query, driveService, cancellationToken).ConfigureAwait(false);

            var file = matchingFiles.FirstOrDefault();

            if (file == null)
            {
                var message = string.Format("Couldn't find file {0}/{1}", parentFolderId, name);
                throw new FileNotFoundException(message, name);
            }

            return file;
        }

        public async Task<QueryResult<FileSystemMetadata>> GetFilesInFolder(string folderId, GoogleCredentials googleCredentials,
             DriveService driveService, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(folderId))
            {
                throw new ArgumentNullException(nameof(folderId));
            }

            var query = string.Format("'{0}' in parents", folderId);

            var matchingFiles = await GetFiles(query, driveService, cancellationToken).ConfigureAwait(false);

            var result = new QueryResult<FileSystemMetadata>();

            if (matchingFiles != null)
            {
                result.Items = matchingFiles.Select(GetFileMetadata).ToArray();
                result.TotalRecordCount = result.Items.Length;
            }

            return result;
        }

        private async Task<List<File>> GetFiles(string query, DriveService driveService, CancellationToken cancellationToken)
        {
            var request = driveService.Files.List();
            request.Q = query;

            var files = await GetAllFiles(request, cancellationToken).ConfigureAwait(false);
            return files;
        }

        private static async Task<List<File>> GetAllFiles(FilesResource.ListRequest request, CancellationToken cancellationToken)
        {
            var result = new List<File>();

            do
            {
                result.AddRange(await GetFilesPage(request, cancellationToken).ConfigureAwait(false));
            } while (!string.IsNullOrEmpty(request.PageToken));

            return result;
        }

        private static async Task<IEnumerable<File>> GetFilesPage(FilesResource.ListRequest request, CancellationToken cancellationToken)
        {
            var files = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            request.PageToken = files.NextPageToken;
            return files.Items;
        }

        private Tuple<DriveService, UserCredential> CreateDriveServiceAndCredentials(GoogleCredentials googleCredentials)
        {
            var authorizationCodeFlowInitializer = new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = googleCredentials.ClientId,
                    ClientSecret = googleCredentials.ClientSecret
                }
            };
            var googleAuthorizationCodeFlow = new GoogleAuthorizationCodeFlow(authorizationCodeFlowInitializer);
            var token = new TokenResponse { RefreshToken = googleCredentials.RefreshToken };
            var credentials = new UserCredential(googleAuthorizationCodeFlow, "user", token);

            var initializer = new BaseClientService.Initializer
            {
                ApplicationName = "Emby",
                HttpClientInitializer = credentials
            };

            var service = new DriveService(initializer)
            {
                HttpClient = { Timeout = TimeSpan.FromHours(1) }
            };

            return new Tuple<DriveService, UserCredential>(service, credentials);
        }

        private static File CreateFileToUpload(GoogleDriveFile googleDriveFile)
        {
            return new File
            {
                Title = googleDriveFile.Name,
                Parents = new List<ParentReference> { new ParentReference { Kind = "drive#fileLink", Id = googleDriveFile.GoogleDriveFolderId } },
                Permissions = new List<Permission> { new Permission { Role = "reader", Type = "anyone" } }
            };
        }

        private static Task ExecuteUpload(DriveService driveService, Stream stream, File file, IProgress<double> progress, CancellationToken cancellationToken)
        {
            var request = driveService.Files.Insert(file, stream, "application/octet-stream");

            var streamLength = stream.Length;
            request.ProgressChanged += (uploadProgress) => progress.Report((double)uploadProgress.BytesSent / streamLength * 100);

            return request.UploadAsync(cancellationToken);
        }

        private async Task<File> FindFolder(string name, string parentId, DriveService driveService, CancellationToken cancellationToken)
        {
            name = name.Replace("'", "\\'");
            var query = string.Format(@"title = '{0}' and properties has {{ key='{1}' and value='{2}' and visibility='PRIVATE' }}", name, SyncFolderPropertyKey, SyncFolderPropertyValue);

            if (!string.IsNullOrWhiteSpace(parentId))
            {
                query += string.Format(" and '{0}' in parents", parentId);
            }
            var matchingFolders = await GetFiles(query, driveService, cancellationToken).ConfigureAwait(false);

            return matchingFolders.FirstOrDefault();
        }

        private static async Task<string> CreateFolder(string name, string parentId, CancellationToken cancellationToken, DriveService driveService)
        {
            var file = CreateFolderToUpload(name, parentId);

            var request = driveService.Files.Insert(file);
            var newFolder = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            return newFolder.Id;
        }

        private static File CreateFolderToUpload(string name, string parentId)
        {
            var property = new Property
            {
                Key = SyncFolderPropertyKey,
                Value = SyncFolderPropertyValue,
                Visibility = "PRIVATE"
            };

            File file = new File
            {
                Title = name,
                MimeType = "application/vnd.google-apps.folder",
                Properties = new List<Property> { property }
            };

            if (!string.IsNullOrWhiteSpace(parentId))
            {
                file.Parents = new List<ParentReference>
                {
                    new ParentReference
                    {
                       Id = parentId
                    }
                };
            }

            return file;
        }
    }
}
