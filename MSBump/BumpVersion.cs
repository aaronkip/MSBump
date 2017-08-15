﻿using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace MSBump
{
    public class BumpVersion : Task
    {
        private int GetNextValue(int oldValue, bool bump, bool reset)
        {
            if (reset)
                return 0;
            if (bump)
                return oldValue + 1;
            return oldValue;
        }

        public override bool Execute()
        {
            try
            {
                var proj = XDocument.Load(ProjectPath, LoadOptions.PreserveWhitespace);
                Settings settings = null;
                var settingsFilePath = Path.ChangeExtension(ProjectPath, ".msbump");
                if (!string.IsNullOrEmpty(settingsFilePath) && File.Exists(settingsFilePath))
                {
                    var settingsCollection = JsonSerializer.Create()
                        .Deserialize<SettingsCollection>(new JsonTextReader(File.OpenText(settingsFilePath)));
                    if (!string.IsNullOrEmpty(Configuration))
                        settingsCollection.Configurations?.TryGetValue(Configuration, out settings);
                    if (settings == null)
                        settings = settingsCollection;
                }
                if (settings == null)
                    settings = new Settings
                    {
                        BumpMajor = BumpMajor,
                        BumpMinor = BumpMinor,
                        BumpPatch = BumpPatch,
                        BumpRevision = BumpRevision,
                        BumpLabel = BumpLabel,
                        ResetMajor = ResetMajor,
                        ResetMinor = ResetMinor,
                        ResetPatch = ResetPatch,
                        ResetRevision = ResetRevision,
                        ResetLabel = ResetLabel,
                        LabelDigits = LabelDigits == 0 ? Settings.DefaultLabelDigits : LabelDigits
                    };

                var xversion = proj.Root.XPathSelectElement("PropertyGroup/Version");
                if (xversion == null)
                {
                    BuildEngine.LogMessageEvent(new BuildMessageEventArgs(
                        $"Version property not found in {ProjectPath}",
                        null, GetType().Name, MessageImportance.Low));
                    return true;
                }
                BuildEngine.LogMessageEvent(new BuildMessageEventArgs($"Old version is {xversion.Value}", null,
                    GetType().Name, MessageImportance.Low));
                var version = new NuGetVersion(xversion.Value);
                int major = version.Major;
                int minor = version.Minor;
                int patch = version.Patch;
                int revision = version.Revision;
                var labels = version.ReleaseLabels.ToList();

                major = GetNextValue(major, settings.BumpMajor, settings.ResetMajor);
                minor = GetNextValue(minor, settings.BumpMinor, settings.ResetMinor);
                patch = GetNextValue(patch, settings.BumpPatch, settings.ResetPatch);
                revision = GetNextValue(revision, settings.BumpRevision, settings.ResetRevision);

                if (!string.IsNullOrEmpty(settings.ResetLabel))
                {
                    if (!settings.ResetLabel.All(Char.IsLetterOrDigit))
                    {
                        BuildEngine.LogMessageEvent(
                            new BuildMessageEventArgs(
                                $"Invalid version label for {GetType().Name}: {settings.ResetLabel} - only alphanumeric characters are allowed",
                                null, GetType().Name, MessageImportance.High));
                        return false;
                    }
                    var regex = new Regex($"^{settings.ResetLabel}(\\d*)$");
                    foreach (var label in labels)
                    {
                        var match = regex.Match(label);
                        if (match.Success)
                        {
                            labels.Remove(label);
                            break;
                        }
                    }
                }
                // Find and modify the release label selected with `BumpLabel`
                // If ResetLabel is true, remove only the specified label.
                if (!string.IsNullOrEmpty(settings.BumpLabel) && settings.BumpLabel != settings.ResetLabel)
                {
                    if (!settings.BumpLabel.All(Char.IsLetterOrDigit))
                    {
                        BuildEngine.LogMessageEvent(
                            new BuildMessageEventArgs(
                                $"Invalid version label for {GetType().Name}: {settings.BumpLabel} - only alphanumeric characters are allowed",
                                null, GetType().Name, MessageImportance.High));
                        return false;
                    }
                    var regex = new Regex($"^{settings.BumpLabel}(\\d*)$");
                    var value = 0;
                    foreach (var label in labels)
                    {
                        var match = regex.Match(label);
                        if (match.Success)
                        {
                            if (!string.IsNullOrEmpty(match.Groups[1].Value))
                                value = int.Parse(match.Groups[1].Value);
                            labels.Remove(label);
                            break;
                        }
                    }
                    value++;
                    labels.Add(settings.BumpLabel + value.ToString(new string('0', settings.LabelDigits)));
                }
                var newVersion = new NuGetVersion(major, minor, patch, revision, labels, version.Metadata);
                if (newVersion != version)
                {
                    BuildEngine.LogMessageEvent(
                        new BuildMessageEventArgs($"Changing project version to {newVersion.ToString()}...", null,
                            GetType().Name, MessageImportance.High));
                    xversion.Value = newVersion.ToString();
                    using (var stream = File.Create(ProjectPath))
                        proj.Save(stream);
                }
                NewVersion = newVersion.ToString();
            }
            catch (Exception e)
            {
                BuildEngine.LogMessageEvent(
                    new BuildMessageEventArgs(e.Message, null,
                        GetType().Name, MessageImportance.High));
                return false;
            }
            return true;
        }

        [Required]
        public string ProjectPath { get; set; }

        public string Configuration { get; set; }

        public bool BumpMajor { get; set; }

        public bool BumpMinor { get; set; }

        public bool BumpPatch { get; set; }

        public bool BumpRevision { get; set; }

        public string BumpLabel { get; set; }

        public bool ResetMajor { get; set; }

        public bool ResetMinor { get; set; }

        public bool ResetPatch { get; set; }

        public bool ResetRevision { get; set; }

        public string ResetLabel { get; set; }

        public int LabelDigits { get; set; } = 6;

        [Output]
        public string NewVersion { get; set; }
    }
}
