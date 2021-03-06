﻿using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Plugins.GoogleDrive.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Common;

namespace MediaBrowser.Plugins.GoogleDrive
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public static Plugin Instance { get; private set; }

        public IConfigurationRetriever ConfigurationRetriever = new ConfigurationRetriever();
        public IGoogleAuthService GoogleAuthService;
        public IGoogleDriveService GoogleDriveService = new GoogleDriveService();

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, IHttpClient httpClient, IJsonSerializer jsonSerializer, IApplicationHost appHost)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            GoogleAuthService = new GoogleAuthService(httpClient, jsonSerializer, appHost);
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
                }
            };
        }

        public override string Name
        {
            get { return Constants.Name; }
        }

        public override string Description
        {
            get { return Constants.Description; }
        }

        private Guid _id = new Guid("b2ff6a63-303a-4a84-b937-6e12f87e3eb9");
        public override Guid Id
        {
            get { return _id; }
        }
    }
}
