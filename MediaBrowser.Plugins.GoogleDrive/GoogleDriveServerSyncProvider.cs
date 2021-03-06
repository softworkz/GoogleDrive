﻿using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Sync;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Sync;
using MediaBrowser.Plugins.GoogleDrive.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.IO;

namespace MediaBrowser.Plugins.GoogleDrive
{
    public class GoogleDriveServerSyncProvider : IServerSyncProvider, IHasDynamicAccess, IRemoteSyncProvider
    {
        private IConfigurationRetriever _configurationRetriever
        {
            get
            {
                return Plugin.Instance.ConfigurationRetriever;
            }
        }
        private IGoogleDriveService _googleDriveService
        {
            get
            {
                return Plugin.Instance.GoogleDriveService;
            }
        }
        private readonly ILogger _logger;
        private readonly IHttpClient _httpClient;

        public GoogleDriveServerSyncProvider(ILogManager logManager, IHttpClient httpClient)
        {
            _httpClient = httpClient;
            _logger = logManager.GetLogger("GoogleDrive");
        }

        public string Name
        {
            get { return Constants.Name; }
        }

        public bool SupportsRemoteSync
        {
            get { return true; }
        }

        public List<SyncTarget> GetAllSyncTargets()
        {
            return _configurationRetriever.GetSyncAccounts().Select(CreateSyncTarget).ToList();
        }

        public List<SyncTarget> GetSyncTargets(string userId)
        {
            return _configurationRetriever.GetUserSyncAccounts(userId).Select(CreateSyncTarget).ToList();
        }

        public async Task<SyncedFileInfo> SendFile(SyncJob syncJob, string originalMediaPath, Stream inputStream, bool isMedia, string[] outputPathParts, SyncTarget target, IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.Debug("Sending file {0} to {1}", string.Join("/", outputPathParts), target.Name);

            var syncAccount = _configurationRetriever.GetSyncAccount(target.Id);

            var googleCredentials = GetGoogleCredentials(target);

            var file = await _googleDriveService.UploadFile(inputStream, outputPathParts, syncAccount.FolderId, googleCredentials, progress, cancellationToken);

            return new SyncedFileInfo
            {
                Path = file.Item2,
                Protocol = MediaProtocol.Http,
                Id = file.Item1
            };
        }

        public string GetFullPath(IEnumerable<string> path, SyncTarget target)
        {
            return Path.Combine(path.ToArray());
        }

        public async Task<SyncedFileInfo> GetSyncedFileInfo(string id, SyncTarget target, CancellationToken cancellationToken)
        {
            _logger.Debug("Getting synced file info for {0} from {1}", id, target.Name);

            var googleCredentials = GetGoogleCredentials(target);

            var url = await _googleDriveService.CreateDownloadUrl(id, googleCredentials, cancellationToken).ConfigureAwait(false);

            return new SyncedFileInfo
            {
                Path = url,
                Protocol = MediaProtocol.Http,
                Id = id
            };
        }

        public Task DeleteFile(string id, SyncTarget target, CancellationToken cancellationToken)
        {
            var googleCredentials = GetGoogleCredentials(target);

            return _googleDriveService.DeleteFile(id, googleCredentials, cancellationToken);
        }

        public async Task<Stream> GetFile(string id, SyncTarget target, IProgress<double> progress, CancellationToken cancellationToken)
        {
            var googleCredentials = GetGoogleCredentials(target);
            var url = await _googleDriveService.CreateDownloadUrl(id, googleCredentials, cancellationToken).ConfigureAwait(false);

            return await _httpClient.Get(new HttpRequestOptions
            {
                Url = url,
                BufferContent = false,
                CancellationToken = cancellationToken

            }).ConfigureAwait(false);

            //return await _googleDriveService.GetFile(file, googleCredentials, cancellationToken);
        }

        private SyncTarget CreateSyncTarget(GoogleDriveSyncAccount syncAccount)
        {
            return new SyncTarget
            {
                Id = syncAccount.Id,
                Name = syncAccount.Name
            };
        }

        private GoogleCredentials GetGoogleCredentials(SyncTarget target)
        {
            var syncAccount = _configurationRetriever.GetSyncAccount(target.Id);
            var generalConfig = _configurationRetriever.GetGeneralConfiguration();

            return new GoogleCredentials
            {
                RefreshToken = syncAccount.RefreshToken,
                ClientId = generalConfig.GoogleDriveClientId,
                ClientSecret = generalConfig.GoogleDriveClientSecret
            };
        }

        public Task<QueryResult<FileSystemMetadata>> GetFiles(string[] directoryPathParts, SyncTarget target, CancellationToken cancellationToken)
        {
            var googleCredentials = GetGoogleCredentials(target);
            var syncAccount = _configurationRetriever.GetSyncAccount(target.Id);

            return _googleDriveService.GetFiles(directoryPathParts, syncAccount.FolderId, googleCredentials, cancellationToken);
        }

        public Task<QueryResult<FileSystemMetadata>> GetFiles(SyncTarget target, CancellationToken cancellationToken)
        {
            var googleCredentials = GetGoogleCredentials(target);
            var syncAccount = _configurationRetriever.GetSyncAccount(target.Id);

            return _googleDriveService.GetFiles(syncAccount.FolderId, googleCredentials, cancellationToken);
        }
    }
}
