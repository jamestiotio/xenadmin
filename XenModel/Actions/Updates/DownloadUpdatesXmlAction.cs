﻿/* Copyright (c) Cloud Software Group, Inc. 
 * 
 * Redistribution and use in source and binary forms, 
 * with or without modification, are permitted provided 
 * that the following conditions are met: 
 * 
 * *   Redistributions of source code must retain the above 
 *     copyright notice, this list of conditions and the 
 *     following disclaimer. 
 * *   Redistributions in binary form must reproduce the above 
 *     copyright notice, this list of conditions and the 
 *     following disclaimer in the documentation and/or other 
 *     materials provided with the distribution. 
 * 
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND 
 * CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, 
 * INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF 
 * MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE 
 * DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR 
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, 
 * SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, 
 * BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR 
 * SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, 
 * WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING 
 * NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE 
 * OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF 
 * SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Xml;
using XenAdmin.Core;
using System.Diagnostics;
using System.Net;
using System.Net.Cache;


namespace XenAdmin.Actions
{
    public class DownloadClientUpdatesXmlAction : DownloadUpdatesXmlAction
    {
        private const string ClientVersionsNode = "versions";

        private readonly bool _checkForXenCenter;

        public DownloadClientUpdatesXmlAction(bool checkForXenCenter, string userAgent, string xmlLocationUrl, bool suppressHistory)
            : base(userAgent, xmlLocationUrl, suppressHistory)
        {
            _checkForXenCenter = checkForXenCenter;
            Title = Description = string.Format(Messages.AVAILABLE_UPDATES_CHECKING, BrandManager.BrandConsole);
        }

        public List<ClientVersion> ClientVersions { get; } = new List<ClientVersion>();

        protected override void Run()
        {
            try
            {
                XmlDocument xdoc = FetchCheckForUpdatesXml();

                GetXenCenterVersions(xdoc);

                Description = Messages.COMPLETED;
            }
            catch (Exception e)
            {
                if (e is System.Net.Sockets.SocketException)
                {
                    Description = Messages.AVAILABLE_UPDATES_NETWORK_ERROR;
                }
                else if (!string.IsNullOrWhiteSpace(e.Message))
                {
                    string errorText = e.Message.Trim();
                    errorText = System.Text.RegularExpressions.Regex.Replace(errorText, @"\r\n+", "");
                    Description = string.Format(Messages.AVAILABLE_UPDATES_ERROR, errorText);
                }
                else
                {
                    Description = Messages.AVAILABLE_UPDATES_INTERNAL_ERROR;
                }

                //if we had originally wanted it to be hidden, make it visible now so the error is shown
                if (SuppressHistory)
                    SuppressHistory = false;

                throw;
            }
        }

        private void GetXenCenterVersions(XmlDocument xdoc)
        {
            if (!_checkForXenCenter)
                return;

            foreach (XmlNode versions in xdoc.GetElementsByTagName(ClientVersionsNode))
            {
                foreach (XmlNode version in versions.ChildNodes)
                {
                    string versionLang = string.Empty;
                    string name = string.Empty;
                    bool latest = false;
                    bool latestCr = false;
                    string url = string.Empty;
                    string timestamp = string.Empty;
                    string checksum = string.Empty;

                    foreach (XmlAttribute attrib in version.Attributes)
                    {
                        if (attrib.Name == "value")
                            versionLang = attrib.Value;
                        else if (attrib.Name == "name")
                            name = attrib.Value;
                        else if (attrib.Name == "latest")
                            latest = attrib.Value.ToUpperInvariant() == bool.TrueString.ToUpperInvariant();
                        else if (attrib.Name == "latestcr")
                            latestCr = attrib.Value.ToUpperInvariant() == bool.TrueString.ToUpperInvariant();
                        else if (attrib.Name == "url")
                            url = attrib.Value;
                        else if (attrib.Name == "timestamp")
                            timestamp = attrib.Value;
                        else if (attrib.Name == "checksum")
                            checksum = attrib.Value;
                    }

                    ClientVersions.Add(new ClientVersion(versionLang, name, latest, latestCr, url, timestamp, checksum));
                }
            }
        }
    }


    public class DownloadCfuAction : DownloadUpdatesXmlAction
    {
        private const string XenServerVersionsNode = "serverversions";
        private const string PatchesNode = "patches";
        private const string ConflictingPatchesNode = "conflictingpatches";
        private const string RequiredPatchesNode = "requiredpatches";
        private const string ConflictingPatchNode = "conflictingpatch";
        private const string RequiredPatchNode = "requiredpatch";

        private readonly bool _checkForServerVersion;
        private readonly bool _checkForPatches;

        public DownloadCfuAction(bool checkForServerVersion, bool checkForPatches, string userAgent, string xmlLocationUrl, bool suppressHistory)
            : base(userAgent, xmlLocationUrl, suppressHistory)
        {
            _checkForServerVersion = checkForServerVersion;
            _checkForPatches = checkForPatches;
            Title = Description = string.Format(Messages.AVAILABLE_UPDATES_CHECKING, BrandManager.ProductBrand);
        }

        public List<XenServerVersion> XenServerVersions { get; } = new List<XenServerVersion>();
        public List<XenServerPatch> XenServerPatches { get; } = new List<XenServerPatch>();

        public List<XenServerVersion> XenServerVersionsForAutoCheck =>
            _checkForServerVersion ? XenServerVersions : new List<XenServerVersion>();

        protected override void Run()
        {
            try
            {
                XmlDocument xdoc = FetchCheckForUpdatesXml();

                GetXenServerPatches(xdoc);
                GetXenServerVersions(xdoc);

                Description = Messages.COMPLETED;
            }
            catch (Exception e)
            {
                if (e is System.Net.Sockets.SocketException)
                {
                    Description = Messages.AVAILABLE_UPDATES_NETWORK_ERROR;
                }
                else if (!string.IsNullOrWhiteSpace(e.Message))
                {
                    string errorText = e.Message.Trim();
                    errorText = System.Text.RegularExpressions.Regex.Replace(errorText, @"\r\n+", "");
                    Description = string.Format(Messages.AVAILABLE_UPDATES_ERROR, errorText);
                }
                else
                {
                    Description = Messages.AVAILABLE_UPDATES_INTERNAL_ERROR;
                }

                //if we had originally wanted it to be hidden, make it visible now so the error is shown
                if (SuppressHistory)
                    SuppressHistory = false;

                throw;
            }
        }

        private void GetXenServerPatches(XmlDocument xdoc)
        {
            if (!_checkForPatches)
                return;

            foreach (XmlNode versions in xdoc.GetElementsByTagName(PatchesNode))
            {
                foreach (XmlNode version in versions.ChildNodes)
                {
                    string uuid = "";
                    string name = "";
                    string description = "";
                    string guidance = "";
                    string guidance_mandatory = "";
                    string patchVersion = "";
                    string url = "";
                    string patchUrl = "";
                    string timestamp = "";
                    string priority = "";
                    string installationSize = "";
                    string downloadSize = "";
                    string containsLivepatch = "";

                    foreach (XmlAttribute attrib in version.Attributes)
                    {
                        if (attrib.Name == "uuid")
                            uuid = attrib.Value;
                        else if (attrib.Name == "name-label")
                            name = attrib.Value;
                        else if (attrib.Name == "name-description")
                            description = attrib.Value;
                        else if (attrib.Name == "after-apply-guidance")
                            guidance = attrib.Value;
                        else if (attrib.Name == "guidance-mandatory")
                            guidance_mandatory = attrib.Value;
                        else if (attrib.Name == "version")
                            patchVersion = attrib.Value;
                        else if (attrib.Name == "url")
                            url = attrib.Value;
                        else if (attrib.Name == "patch-url")
                            patchUrl = attrib.Value;
                        else if (attrib.Name == "timestamp")
                            timestamp = attrib.Value;
                        else if (attrib.Name == "priority")
                            priority = attrib.Value;
                        else if (attrib.Name == "installation-size")
                            installationSize = attrib.Value;
                        else if (attrib.Name == "download-size")
                            downloadSize = attrib.Value;
                        else if (attrib.Name == "contains-livepatch")
                            containsLivepatch = attrib.Value;
                    }

                    var conflictingPatches = GetPatchDependencies(version, ConflictingPatchesNode, ConflictingPatchNode);
                    var requiredPatches = GetPatchDependencies(version, RequiredPatchesNode, RequiredPatchNode);

                    XenServerPatches.Add(new XenServerPatch(uuid, name, description, guidance, guidance_mandatory, patchVersion, url,
                                                            patchUrl, timestamp, priority, installationSize, downloadSize, containsLivepatch, conflictingPatches, requiredPatches));
                }
            }
        }

        // dependencies patches are listed in the xml as below:
        // <conflictingpatches>
        //    <conflictingpatch uuid="00000000-0000-0000-0000-000000000000">
        //    </conflictingpatch>
        // </conflictingpatches>
        // <requiredpatches>
        //    <requiredgpatch uuid="00000000-0000-0000-0000-000000000000">
        //    </requiredgpatch>
        // </requiredpatches>
        private static List<string> GetPatchDependencies(XmlNode patchsNode, string dependenciesNodeName, string dependencyNodeName)
        {
            var dependenciesNode = patchsNode.ChildNodes.Cast<XmlNode>().FirstOrDefault(childNode => childNode.Name == dependenciesNodeName);

            if (dependenciesNode == null)
                return null;

            var dependencies = new List<string>();

            dependencies.AddRange(from XmlNode node in dependenciesNode.ChildNodes
                                  where node.Attributes != null
                                  from XmlAttribute attrib in node.Attributes
                                  where node.Name == dependencyNodeName && node.Attributes != null && attrib.Name == "uuid"
                                  select attrib.Value);
            return dependencies;
        }

        private void GetXenServerVersions(XmlDocument xdoc)
        {
            if (!_checkForServerVersion && !_checkForPatches)
                return;

            foreach (XmlNode versions in xdoc.GetElementsByTagName(XenServerVersionsNode))
            {
                foreach (XmlNode version in versions.ChildNodes)
                {
                    string version_oem = "";
                    string name = "";
                    bool is_latest = false;
                    bool is_latest_cr = false;
                    string url = "";
                    string timestamp = "";
                    string buildNumber = "";
                    string patchUuid = "";
                    bool presentAsUpdate = false;
                    string minXcVersion = "";
                    string hotfixEligibility = "";
                    string hotfixEligibilityPremiumDate = "";
                    string hotfixEligibilityNoneDate = "";
                    string eolDate = "";


                    foreach (XmlAttribute attrib in version.Attributes)
                    {
                        if (attrib.Name == "value")
                            version_oem = attrib.Value;
                        else if (attrib.Name == "name")
                            name = attrib.Value;
                        else if (attrib.Name == "latest")
                            is_latest = attrib.Value.ToUpperInvariant() == bool.TrueString.ToUpperInvariant();
                        else if (attrib.Name == "latestcr")
                            is_latest_cr = attrib.Value.ToUpperInvariant() == bool.TrueString.ToUpperInvariant();
                        else if (attrib.Name == "url")
                            url = attrib.Value;
                        else if (attrib.Name == "timestamp")
                            timestamp = attrib.Value;
                        else if (attrib.Name == "build-number")
                            buildNumber = attrib.Value;
                        else if (attrib.Name == "patch-uuid")
                            patchUuid = attrib.Value;
                        else if (attrib.Name == "present-as-update")
                            presentAsUpdate = attrib.Value.ToUpperInvariant() == bool.TrueString.ToUpperInvariant();
                        else if (attrib.Name == "minimum-xc-version")
                            minXcVersion = attrib.Value;
                        else if (attrib.Name == "hotfix-eligibility")
                            hotfixEligibility = attrib.Value;
                        else if (attrib.Name == "hotfix-eligibility-premium-date")
                            hotfixEligibilityPremiumDate = attrib.Value;
                        else if (attrib.Name == "hotfix-eligibility-none-date")
                            hotfixEligibilityNoneDate = attrib.Value;
                        else if (attrib.Name == "eol-date")
                            eolDate = attrib.Value;
                    }

                    List<XenServerPatch> patches = new List<XenServerPatch>();
                    List<XenServerPatch> minimalPatches = null; //keep it null to indicate that there is no a minimalpatches tag

                    foreach (XmlNode childnode in version.ChildNodes)
                    {
                        if (childnode.Name == "minimalpatches")
                        {
                            minimalPatches = new List<XenServerPatch>();

                            foreach (XmlNode minimalpatch in childnode.ChildNodes)
                            {
                                if (minimalpatch.Name != "patch")
                                    continue;

                                XenServerPatch mp = XenServerPatches.Find(p => string.Equals(p.Uuid, minimalpatch.Attributes["uuid"].Value, StringComparison.OrdinalIgnoreCase));
                                if (mp == null)
                                    continue;
                                minimalPatches.Add(mp);
                            }
                        }

                        if (childnode.Name == "patch")
                        {

                            XenServerPatch patch = XenServerPatches.Find(item => string.Equals(item.Uuid, childnode.Attributes["uuid"].Value, StringComparison.OrdinalIgnoreCase));
                            if (patch == null)
                                continue;
                            patches.Add(patch);
                        }

                    }

                    XenServerVersions.Add(new XenServerVersion(version_oem, name, is_latest, is_latest_cr, url, patches, minimalPatches, timestamp,
                                                               buildNumber, patchUuid, presentAsUpdate, minXcVersion, hotfixEligibility, hotfixEligibilityPremiumDate, hotfixEligibilityNoneDate, eolDate));
                }
            }
        }
    }


    public abstract class DownloadUpdatesXmlAction : AsyncAction
    {
        private readonly string _userAgent;
        private readonly string _checkForUpdatesUrl;

        protected DownloadUpdatesXmlAction(string userAgent, string xmlLocationUrl, bool suppressHistory)
            : base(null, string.Empty, string.Empty, suppressHistory)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(userAgent));
            _userAgent = userAgent;
            _checkForUpdatesUrl = xmlLocationUrl;
        }

        protected XmlDocument FetchCheckForUpdatesXml()
        {
            var checkForUpdatesXml = new XmlDocument();
            var uriBuilder = new UriBuilder(_checkForUpdatesUrl);

            var uri = uriBuilder.Uri;
            if (uri.IsFile)
            {
                checkForUpdatesXml.Load(_checkForUpdatesUrl);
            }
            else
            {
                var authToken = XenAdminConfigManager.Provider.GetClientUpdatesQueryParam();
                uriBuilder.Query = Helpers.AddAuthTokenToQueryString(authToken, uriBuilder.Query);

                var proxy = XenAdminConfigManager.Provider.GetProxyFromSettings(Connection, false);

                using (var webClient = new WebClient())
                {
                    webClient.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);

                    webClient.Proxy = proxy;
                    webClient.Headers.Add("User-Agent", _userAgent);
                    using (var stream = new MemoryStream(webClient.DownloadData(uriBuilder.Uri)))
                        checkForUpdatesXml.Load(stream);
                }
            }

            return checkForUpdatesXml;
        }
    }
}
