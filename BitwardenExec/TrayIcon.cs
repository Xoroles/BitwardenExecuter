using System.Reflection;
using Microsoft.Extensions.Hosting;
using Application = System.Windows.Application;

namespace BitwardenExec
{
    public class TrayIcon
    {
        private readonly IHost _host;
        private readonly NotifyIcon _trayIcon;

        public TrayIcon(IHost host)
        {
            this._host = host;
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("BitwardenExec.Resources.BitwardenTeamViewer.ico");
            this._trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "Web API läuft im Hintergrund",
                Visible = true,
                ContextMenuStrip = this.CreateContextMenu(),
            };
            this._trayIcon.Icon = new Icon(stream);

        }

        public void Show()
        {
            this._trayIcon.Visible = true;
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();

            var exitItem = new ToolStripMenuItem("Beenden");
            exitItem.Click += (sender, args) =>
            {
                this._trayIcon.Visible = false;
                //this._host.StopAsync().Wait();
                Application.Current.Shutdown();
            };

            menu.Items.Add(exitItem);

            return menu;
        }

        public void Dispose()
        {
            this._trayIcon.Dispose();
        }
    }
}