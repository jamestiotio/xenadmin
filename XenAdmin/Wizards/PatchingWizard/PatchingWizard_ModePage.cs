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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using XenAdmin.Controls;
using XenAdmin.Core;
using XenAPI;


namespace XenAdmin.Wizards.PatchingWizard
{
    public partial class PatchingWizard_ModePage : XenTabPage
    {
        private bool _tooltipShowing;

        public PatchingWizard_ModePage()
        {
            InitializeComponent();
            AutomaticRadioButton.Text = string.Format(AutomaticRadioButton.Text, BrandManager.BrandConsole);
            removeUpdateFileCheckBox.Text = string.Format(removeUpdateFileCheckBox.Text, BrandManager.BrandConsole);
        }

        public override string Text => Messages.PATCHINGWIZARD_MODEPAGE_TEXT;

        public override string PageTitle => Messages.PATCHINGWIZARD_MODEPAGE_TITLE;

        public override bool EnablePrevious()
        {
            return true;
        }

        public override string NextText(bool isLastPage)
        {
            return WizardMode == WizardMode.AutomatedUpdates ? Messages.UPDATES_WIZARD_APPLY_UPDATES : Messages.UPDATES_WIZARD_APPLY_UPDATE;
        }

        protected override void PageLoadedCore(PageLoadedDirection direction)
        {
            removeUpdateFileCheckBox.Visible = !IsNewGeneration;

            var anyPoolForbidsAutostart = SelectedServers.Select(s => Helpers.GetPoolOfOne(s.Connection)).Any(p => p.IsAutoUpdateRestartsForbidden());

            // this will be true if a patch has restartHost guidance or if livepatching fails
            bool someHostMayRequireRestart;
            bool automaticDisabled;

            switch (SelectedUpdateType)
            {
                case UpdateType.Legacy:
                    if (IsNewGeneration)
                    {
                        ManualTextInstructions = ModeCdnUpdates();
                        automaticDisabled = anyPoolForbidsAutostart;
                    }
                    else
                    {
                        ManualTextInstructions = ModePoolPatch(out someHostMayRequireRestart);
                        automaticDisabled = anyPoolForbidsAutostart && someHostMayRequireRestart;
                    }

                    break;
                case UpdateType.ISO:
                    ManualTextInstructions = PoolUpdate != null
                        ? ModePoolUpdate(out someHostMayRequireRestart)
                        : ModeSuppPack(out someHostMayRequireRestart);
                    automaticDisabled = anyPoolForbidsAutostart && someHostMayRequireRestart;
                    break;
                default:
                    ManualTextInstructions = null;
                    automaticDisabled = true;
                    break;
            }

            if (ManualTextInstructions == null || ManualTextInstructions.Count == 0)
            {
                textBoxLog.Text = Messages.PATCHINGWIZARD_MODEPAGE_NOACTION;
            }
            else
            {
                var sb = new StringBuilder();
                foreach (var kvp in ManualTextInstructions)
                {
                    sb.AppendFormat("{0}:", kvp.Key).AppendLine();
                    sb.AppendIndented(kvp.Value.ToString()).AppendLine();
                }
                textBoxLog.Text = sb.ToString();
            }

            AutomaticRadioButton.Enabled = !automaticDisabled;
            AutomaticRadioButton.Checked = !automaticDisabled;
            ManualRadioButton.Checked = automaticDisabled;

            if (automaticDisabled)
            {
                tableLayoutPanel1.MouseMove += tableLayoutPanel1_MouseMove;
            }

            if (SelectedUpdateType == UpdateType.ISO)
            {
                removeUpdateFileCheckBox.Checked = false;
                removeUpdateFileCheckBox.Visible = false;
            }
        }

        protected override void PageLeaveCore(PageLoadedDirection direction, ref bool cancel)
        {
            tableLayoutPanel1.MouseMove -= tableLayoutPanel1_MouseMove;
        }

        /// <summary>
        /// Display the tooltip over the automatic radio button only if it is disabled
        /// </summary>
        private void tableLayoutPanel1_MouseMove(object sender, MouseEventArgs e)
        {
            var control = tableLayoutPanel1.GetChildAtPoint(e.Location);
            if (control != null && !control.Enabled && control is RadioButton)
            {
                if (_tooltipShowing) return;

                automaticRadioButtonTooltip.Show(Messages.POOL_FORBIDS_AUTOMATIC_RESTARTS,
                    AutomaticRadioButton,
                    e.Location.X, control.Height/2);
                _tooltipShowing = true;
            }
            else
            {
                automaticRadioButtonTooltip.Hide(AutomaticRadioButton);
                _tooltipShowing = false;
            }
        }

        public override bool EnableNext()
        {
            return AutomaticRadioButton.Checked || ManualRadioButton.Checked;
        }

        #region Accessors

        public WizardMode WizardMode { get; set; }
        public bool IsNewGeneration { get; set; }

        public Dictionary<Pool, StringBuilder> ManualTextInstructions { get; private set; }

        public bool IsAutomaticMode => AutomaticRadioButton.Checked;

        public bool RemoveUpdateFile => removeUpdateFileCheckBox.Visible && removeUpdateFileCheckBox.Checked;

        public List<Pool> SelectedPools { private get; set; }
        public List<Host> SelectedServers { private get; set; }
        public Dictionary<string, livepatch_status> LivePatchCodesByHost { private get; set; }
        public Pool_update PoolUpdate { private get; set; }
        public Pool_patch Patch { private get; set; }
        public UpdateType SelectedUpdateType { private get; set; }

        #endregion

        private void AutomaticRadioButton_CheckedChanged(object sender, EventArgs e)
        {
            if (AutomaticRadioButton.Checked)
                OnPageUpdated();
        }

        private void ManualRadioButton_CheckedChanged(object sender, EventArgs e)
        {
            if (ManualRadioButton.Checked)
                OnPageUpdated();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            string filePath = Path.GetTempFileName();

            using (var fileStream = File.Create(filePath))
            using (var sw = new StreamWriter(fileStream))
            {
                sw.Write(textBoxLog.Text);
                sw.Flush();
            }
            Process.Start("notepad.exe", filePath);
        }

        #region Guidance builder

        private Dictionary<Pool, StringBuilder> ModePoolPatch(out bool someHostMayRequireRestart)
        {
            someHostMayRequireRestart = false;

            if (Patch == null || Patch.after_apply_guidance == null || Patch.after_apply_guidance.Count == 0)
                return null;

            var applicableServers = SelectedServers.Where(h => Patch.AppliedOn(h) == DateTime.MaxValue).ToList();
            var serversPerPool = GroupServersPerPool(SelectedPools, applicableServers);

            var total = new Dictionary<Pool, StringBuilder>();

            foreach (var guide in Patch.after_apply_guidance)
            {
                var result = GetGuidanceList(guide, serversPerPool, null, out someHostMayRequireRestart);
                if (result == null)
                    continue;
                foreach (var kvp in result)
                {
                    if (total.ContainsKey(kvp.Key))
                        total[kvp.Key].Append(kvp.Value).AppendLine();
                    else
                        total[kvp.Key] = kvp.Value;
                }
            }

            return total;
        }

        private Dictionary<Pool, StringBuilder> ModePoolUpdate(out bool someHostMayRequireRestart)
        {
            someHostMayRequireRestart = false;

            if (PoolUpdate == null || PoolUpdate.after_apply_guidance == null || PoolUpdate.after_apply_guidance.Count == 0)
                return null;

            var applicableServers = SelectedServers.Where(h => !PoolUpdate.AppliedOn(h)).ToList();
            var serversPerPool = GroupServersPerPool(SelectedPools, applicableServers);

            var total = new Dictionary<Pool, StringBuilder>();

            foreach (var guide in PoolUpdate.after_apply_guidance)
            {
                var result = GetGuidanceList(guide, serversPerPool, LivePatchCodesByHost, out someHostMayRequireRestart);
                if (result == null)
                    continue;
                foreach (var kvp in result)
                {
                    if (total.ContainsKey(kvp.Key))
                        total[kvp.Key].Append(kvp.Value).AppendLine();
                    else
                        total[kvp.Key] = kvp.Value;
                }
            }

            return total;
        }

        private Dictionary<Pool, StringBuilder> ModeSuppPack(out bool someHostMayRequireRestart)
        {
            var serversPerPool = GroupServersPerPool(SelectedPools, SelectedServers);
            return GetGuidanceList(after_apply_guidance.restartHost, serversPerPool, null, out someHostMayRequireRestart);
        }

        private Dictionary<Pool, StringBuilder> ModeCdnUpdates()
        {
            var poolDict = new Dictionary<Pool, StringBuilder>();

            foreach (var pool in SelectedPools)
            {
                if (!Updates.CdnUpdateInfoPerConnection.TryGetValue(pool.Connection, out var poolUpdateInfo))
                    continue;

                var hostDict = new Dictionary<Host, StringBuilder>();

                foreach (var hostUpdateInfo in poolUpdateInfo.HostsWithUpdates)
                {
                    var host = pool.Connection.Resolve(new XenRef<Host>(hostUpdateInfo.HostOpaqueRef));
                    if (host != null)
                    {
                        var hostSb = new StringBuilder();

                        var msg = host.IsCoordinator() ? $"{host.Name()} ({Messages.COORDINATOR})" : host.Name();
                        hostSb.AppendIndented(msg).AppendLine();

                        //evacuate host is a pre-update task and will be done regardless the mode selected

                        if (hostUpdateInfo.RecommendedGuidance.Contains(CdnGuidance.RestartToolstack) ||
                            host.pending_guidances.Contains(update_guidances.restart_toolstack))
                        {
                            hostSb.AppendIndented(Cdn.FriendlyInstruction(CdnGuidance.RestartToolstack), 4).AppendLine();
                        }

                        if (hostUpdateInfo.RecommendedGuidance.Contains(CdnGuidance.RebootHost) ||
                            host.pending_guidances.Contains(update_guidances.reboot_host) ||
                            host.pending_guidances.Contains(update_guidances.reboot_host_on_livepatch_failure))
                        {
                            hostSb.AppendIndented(Cdn.FriendlyInstruction(CdnGuidance.RebootHost), 4).AppendLine();
                        }

                        if (hostUpdateInfo.RecommendedGuidance.Contains(CdnGuidance.RestartDeviceModel) ||
                            host.pending_guidances.Contains(update_guidances.restart_device_model))
                            hostSb.AppendIndented(Cdn.FriendlyInstruction(CdnGuidance.RestartDeviceModel), 4).AppendLine();

                        if (hostUpdateInfo.LivePatches.Length > 0 && !hostUpdateInfo.RecommendedGuidance.Contains(CdnGuidance.RebootHost))
                            hostSb.AppendIndented(Messages.HOTFIX_POST_UPDATE_LIVEPATCH_ACTIONS, 4).AppendLine();

                        hostDict[host] = hostSb;
                    }
                }

                if (hostDict.Count > 0)
                    poolDict[pool] = new StringBuilder(string.Join(Environment.NewLine,
                        hostDict.OrderBy(k => k.Key).Select(k => k.Value.ToString())));
            }

            return poolDict;
        }


        private Dictionary<Pool, List<Host>> GroupServersPerPool(List<Pool> pools, List<Host> servers)
        {
            servers.Sort();
            var dict = new Dictionary<Pool, List<Host>>();

            foreach (var pool in pools)
            {
                var hosts = pool.Connection.Cache.Hosts;
                var hostsPerPool = servers.Where(s => hosts.Contains(s)).ToList();
                if (hostsPerPool.Count > 0)
                    dict[pool] = hostsPerPool;
            }
            return dict;
        }


        private static Dictionary<Pool, StringBuilder> GetGuidanceList(update_after_apply_guidance guide, Dictionary<Pool, List<Host>> serversPerPool, Dictionary<string, livepatch_status> livePatchCodesByHost, out bool someHostMayRequireRestart)
        {
            someHostMayRequireRestart = false;

            switch (guide)
            {
                case update_after_apply_guidance.restartHost:
                    return GetGuidanceListRestartHost(serversPerPool, livePatchCodesByHost, out someHostMayRequireRestart);
                case update_after_apply_guidance.restartXAPI:
                    return GetGuidanceListRestartXapi(serversPerPool);
                case update_after_apply_guidance.restartPV:
                    return GetGuidanceListRestartVm(serversPerPool, vm => !vm.IsHVM());
                case update_after_apply_guidance.restartHVM:
                    return GetGuidanceListRestartVm(serversPerPool, vm => vm.IsHVM());
                default:
                    return null;
            }
        }

        private static Dictionary<Pool, StringBuilder> GetGuidanceList(after_apply_guidance guide, Dictionary<Pool, List<Host>> serversPerPool, Dictionary<string, livepatch_status> livePatchCodesByHost, out bool someHostMayRequireRestart)
        {
            someHostMayRequireRestart = false;

            switch (guide)
            {
                case after_apply_guidance.restartHost:
                    return GetGuidanceListRestartHost(serversPerPool, livePatchCodesByHost, out someHostMayRequireRestart);
                case after_apply_guidance.restartXAPI:
                    return GetGuidanceListRestartXapi(serversPerPool);
                case after_apply_guidance.restartPV:
                    return GetGuidanceListRestartVm(serversPerPool, vm => !vm.IsHVM());
                case after_apply_guidance.restartHVM:
                    return GetGuidanceListRestartVm(serversPerPool, vm => vm.IsHVM());
                default:
                    return null;
            }
        }


        private static Dictionary<Pool, StringBuilder> GetGuidanceListRestartHost(Dictionary<Pool, List<Host>> serversPerPool, Dictionary<string, livepatch_status> livePatchCodesByHost, out bool someHostMayRequireRestart)
        {
            someHostMayRequireRestart = false;
            var dict = new Dictionary<Pool, StringBuilder>();

            foreach (var kvp in serversPerPool)
            {
                var sb = new StringBuilder();
                foreach (var server in kvp.Value)
                {
                    if (livePatchCodesByHost != null && livePatchCodesByHost.ContainsKey(server.uuid)
                        && livePatchCodesByHost[server.uuid] == livepatch_status.ok_livepatch_complete)
                        continue;

                    var msg = server.IsCoordinator()
                        ? string.Format("{0} ({1})", server.Name(), Messages.COORDINATOR)
                        : server.Name();
                    sb.AppendIndented(msg).AppendLine();
                }

                if (sb.Length > 0)
                {
                    sb.Insert(0, Messages.PATCHINGWIZARD_MODEPAGE_RESTARTSERVERS + Environment.NewLine);
                    dict[kvp.Key] = sb;
                    someHostMayRequireRestart = true;
                }
            }
            return dict;
        }

        private static Dictionary<Pool, StringBuilder> GetGuidanceListRestartXapi(Dictionary<Pool, List<Host>> serversPerPool)
        {
            var dict = new Dictionary<Pool, StringBuilder>();

            foreach (var kvp in serversPerPool)
            {
                var sb = new StringBuilder();
                foreach (var server in kvp.Value)
                {
                    var msg = server.IsCoordinator()
                        ? string.Format("{0} ({1})", server.Name(), Messages.COORDINATOR)
                        : server.Name();
                    sb.AppendIndented(msg).AppendLine();
                }

                if (sb.Length > 0)
                {
                    sb.Insert(0, Messages.PATCHINGWIZARD_MODEPAGE_RESTARTXAPI + Environment.NewLine);
                    dict[kvp.Key] = sb;
                }
            }
            return dict;
        }

        private static Dictionary<Pool, StringBuilder> GetGuidanceListRestartVm(Dictionary<Pool, List<Host>> serversPerPool, Func<VM, bool> predicate)
        {
            var dict = new Dictionary<Pool, StringBuilder>();

            foreach (var kvp in serversPerPool)
            {
                var sb = new StringBuilder();
                foreach (var server in kvp.Value)
                {
                    foreach (var vmRef in server.resident_VMs)
                    {
                        var vm = server.Connection.Resolve(vmRef);
                        if (vm != null && vm.IsRealVm() && predicate.Invoke(vm))
                            sb.AppendIndented(vm.Name()).AppendLine();
                    }
                }

                if (sb.Length > 0)
                {
                    sb.Insert(0, Messages.PATCHINGWIZARD_MODEPAGE_RESTARTVMS + Environment.NewLine);
                    dict[kvp.Key] = sb;
                }
            }

            return dict;
        }

        #endregion
    }
}
