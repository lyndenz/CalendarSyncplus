﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CalendarSyncPlus.Common.Log;
using CalendarSyncPlus.Domain.File.Xml;
using CalendarSyncPlus.Domain.Models;
using CalendarSyncPlus.Services.Interfaces;
using log4net;

namespace CalendarSyncPlus.Services
{
    [Export(typeof(ISummarySerializationService))]
    public class SummarySerializationService :ISummarySerializationService
    {
          private readonly ILog _applicationLogger;
        
        private readonly string _applicationDataDirectory;
        private readonly string _settingsFilePath;
       
        [ImportingConstructor]
        public SummarySerializationService(ApplicationLogger applicationLogger)
        {
            _applicationLogger = applicationLogger.GetLogger(GetType());
            _applicationDataDirectory =
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CalendarSyncPlus");
            _settingsFilePath = Path.Combine(_applicationDataDirectory, "Summary.xml");
        }

        #region Properties

        public string SettingsFilePath
        {
            get { return _settingsFilePath; }
        }

        public string ApplicationDataDirectory
        {
            get { return _applicationDataDirectory; }
        }

        #endregion

        private void SerializeSyncSummaryBackgroundTask(SyncSummary syncProfile)
        {
            if (!Directory.Exists(ApplicationDataDirectory))
            {
                Directory.CreateDirectory(ApplicationDataDirectory);
            }

            var serializer = new XmlSerializer<SyncSummary>();
            serializer.SerializeToFile(syncProfile, SettingsFilePath);
        }

        private SyncSummary DeserializeSyncSummaryBackgroundTask()
        {
            if (!File.Exists(SettingsFilePath))
            {
                _applicationLogger.Warn("Sync summary file does not exist");
                return null;
            }
            try
            {
                var serializer = new XmlSerializer<SyncSummary>();
                return serializer.DeserializeFromFile(SettingsFilePath);
            }
            catch (Exception exception)
            {
                _applicationLogger.Error(exception);
                return null;
            }
        }
        public async Task<bool> SerializeSyncSummaryAsync(SyncSummary syncProfile)
        {
            await TaskEx.Run(() => SerializeSyncSummaryBackgroundTask(syncProfile));
            return true;
        }

        public async Task<SyncSummary> DeserializeSyncSummaryAsync()
        {
            if (!File.Exists(SettingsFilePath))
            {
                return null;
            }
            return await TaskEx.Run(() => DeserializeSyncSummaryBackgroundTask());
        }

        public bool SerializeSyncSummary(SyncSummary syncProfile)
        {
            SerializeSyncSummaryBackgroundTask(syncProfile);
            return true;
        }

        public SyncSummary DeserializeSyncSummary()
        {
            SyncSummary result = DeserializeSyncSummaryBackgroundTask();
            if (result == null)
            {
                return SyncSummary.GetDefault();
            }
            return result;
        }
    }
}
