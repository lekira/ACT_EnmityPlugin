﻿using Advanced_Combat_Tracker;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Windows.Forms;
using System.Reflection;
using RainbowMage.HtmlRenderer;
using RainbowMage.OverlayPlugin;
using System;
using System.Drawing;
using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Text.RegularExpressions;
using System.Resources;

namespace Tamagawa.EnmityPlugin
{
    public class PluginMain : IActPluginV1
    {
        TabPage tabPage;
        Label label;
        ControlPanel controlPanel;

        string pluginDirectory;

        internal PluginConfig Config { get; private set; }
        internal EnmityOverlay EnmityOverlay { get; private set; }
        internal BindingList<LogEntry> Logs { get; private set; }

        public PluginMain()
        {
            this.Logs = new BindingList<LogEntry>();

        }

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            try
            {
                pluginScreenSpace.Text = "EnmityPlugin";

                this.tabPage = pluginScreenSpace;
                this.label = pluginStatusText;

#if DEBUG
                Log(LogLevel.Warning, "##################################");
                Log(LogLevel.Warning, "           DEBUG BUILD");
                Log(LogLevel.Warning, "##################################");
#endif
                this.pluginDirectory = GetPluginDirectory();
                Log(LogLevel.Info, "InitPlugin: PluginDirectory = {0}", this.pluginDirectory);

                // プラグインの配置してあるフォルダを検索するカスタムリゾルバーでアセンブリを解決する
                AppDomain.CurrentDomain.AssemblyResolve += CustomAssemblyResolve;

                // アップデートチェック
                this.UpdateCheck();

                // コンフィグ系読み込み
                LoadConfig();
                this.controlPanel = new ControlPanel(this, this.Config);
                this.controlPanel.Dock = DockStyle.Fill;
                this.tabPage.Controls.Add(this.controlPanel);

                // ACT 終了時に CEF をシャットダウン（ゾンビ化防止）
                Application.ApplicationExit += (o, e) =>
                {
                    try { Renderer.Shutdown(); }
                    catch { }
                };

                // オーバーレイ初期化

                this.EnmityOverlay = new EnmityOverlay(this.Config.EnmityOverlay);
                this.EnmityOverlay.OnLog += (o, e) => Log(e.Level, e.Message);
                this.EnmityOverlay.Start();

                // ショートカットキー設定
                ActGlobals.oFormActMain.KeyPreview = true;
                ActGlobals.oFormActMain.KeyDown += oFormActMain_KeyDown;

                Log(LogLevel.Info, "InitPlugin: Initialized.");
                this.label.Text = "Initialized.";
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, "InitPlugin: {0}", e.ToString());
                MessageBox.Show(e.ToString());

                throw;
            }
        }

        public void DeInitPlugin()
        {
            SaveConfig();
            this.EnmityOverlay.Dispose();
            ActGlobals.oFormActMain.KeyDown -= oFormActMain_KeyDown;

            AppDomain.CurrentDomain.AssemblyResolve -= CustomAssemblyResolve;

            Log(LogLevel.Info, "DeInitPlugin: Finalized.");
            this.label.Text = "Finalized.";
        }

        static readonly Regex assemblyNameParser = new Regex(
            @"(?<name>.+?), Version=(?<version>.+?), Culture=(?<culture>.+?), PublicKeyToken=(?<pubkey>.+)", 
            RegexOptions.Compiled);

        private Assembly CustomAssemblyResolve(object sender, ResolveEventArgs e)
        {
            Log(LogLevel.Debug, "AssemblyResolve: Resolving assembly for '{0}'...", e.Name);

            var asmPath = "";
            var match = assemblyNameParser.Match(e.Name);
            if (match.Success)
            {
                var asmFileName = match.Groups["name"].Value + ".dll";
                if (match.Groups["culture"].Value == "neutral")
                {
                    asmPath = Path.Combine(pluginDirectory, asmFileName);
                }
                else
                {
                    asmPath = Path.Combine(pluginDirectory, match.Groups["culture"].Value, asmFileName);
                }
            }
            else
            {
                asmPath = Path.Combine(pluginDirectory, e.Name + ".dll");
            }

            if (File.Exists(asmPath))
            {
                return LoadAssembly(asmPath);
            }

            Log(LogLevel.Debug, "AssemblyResolve: => Not found in plugin directory.");
            return null;
        }

        private Assembly LoadAssembly(string path)
        {
            try
            {
                var result = Assembly.LoadFile(path);
                Log(LogLevel.Debug, "AssemblyResolve: => Found assembly in {0}.", path);
                return result;
            }
            catch (FileLoadException ex)
            {
                var message = string.Format(
                    Messages.RequiredAssemblyFileCannotRead,
                    path
                    );
                Log(LogLevel.Error, "AssemblyResolve: => {0}", message);
                Log(LogLevel.Error, "AssemblyResolve: => {0}", ex);
                MessageBox.Show(message, Messages.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (NotSupportedException ex)
            {
                var message = string.Format(
                    Messages.RequiredAssemblyFileBlocked,
                    path
                    );
                Log(LogLevel.Error, "AssemblyResolve: => {0}", message);
                Log(LogLevel.Error, "AssemblyResolve: => {0}", ex);
                MessageBox.Show(message, Messages.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                var message = string.Format(
                    Messages.RequiredAssemblyFileException,
                    path
                    );
                Log(LogLevel.Error, "AssemblyResolve: => {0}", ex);
                MessageBox.Show(message, Messages.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return null;
        }

        void oFormActMain_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.E)
            {
                // Enmity非表示
                this.Config.EnmityOverlay.IsVisible = !this.Config.EnmityOverlay.IsVisible;
                ActGlobals.oFormActMain.Activate();
            }
        }

        private void LoadConfig()
        {
            try
            {
                Config = PluginConfig.LoadXml(GetConfigPath());
            }
            catch (Exception e)
            {
                Log(LogLevel.Warning, "LoadConfig: {0}", e);
                Log(LogLevel.Info, "LoadConfig: Creating new configuration.");
                Config = new PluginConfig();
            }
            finally
            {
                if (string.IsNullOrWhiteSpace(Config.EnmityOverlay.Url))
                {
                    Config.EnmityOverlay.Url =
                        new Uri(Path.Combine(pluginDirectory, "resources", "enmity.html")).ToString();
                }
            }
        }

        private void SaveConfig()
        {
            try
            {
                Config.EnmityOverlay.Position = this.EnmityOverlay.Overlay.Location;
                Config.EnmityOverlay.Size = this.EnmityOverlay.Overlay.Size;

                Config.SaveXml(GetConfigPath());
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, "SaveConfig: {0}", e);
            }
        }

        private static string GetConfigPath()
        {
            var path = System.IO.Path.Combine(
                ActGlobals.oFormActMain.AppDataFolder.FullName,
                "Config",
                "ACT_EnmityPlugin.config.xml");

            return path;
        }

        private string GetPluginDirectory()
        {
            var plugin = ActGlobals.oFormActMain.ActPlugins.Where(x => x.pluginObj == this).FirstOrDefault();
            if (plugin != null)
            {
                return System.IO.Path.GetDirectoryName(plugin.pluginFile.FullName);
            }
            else
            {
                throw new Exception();
            }
        }

        internal void Log(LogLevel level, string message)
        {
#if !DEBUG
            if (level == LogLevel.Trace || level == LogLevel.Debug)
            {
                return;
            }
#endif
#if DEBUG
            System.Diagnostics.Trace.WriteLine(string.Format("{0}: {1}: {2}", level, DateTime.Now, message));
#endif

            this.Logs.Add(new LogEntry(level, DateTime.Now, message));
        }

        /// <summary>
        /// アップデートチェック
        /// </summary>
        private void UpdateCheck()
        {
            var message = UpdateChecker.Check();
            if (!string.IsNullOrWhiteSpace(message))
            {
                Log(LogLevel.Info, "UpdateChecker: {0}", message);
            }
        }

        internal void Log(LogLevel level, string format, params object[] args)
        {
            Log(level, string.Format(format, args));
        }
    }

    internal class LogEntry
    {
        public string Message { get; set; }
        public LogLevel Level { get; set; }
        public DateTime Time { get; set; }

        public LogEntry(LogLevel level, DateTime time, string message)
        {
            this.Message = message;
            this.Level = level;
            this.Time = time;
        }
    }
}
