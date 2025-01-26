using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BitwardenExec
{
    public partial class App
    {
        private IHost? _host;
        private TrayIcon? _trayIcon;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            this._host = CreateHostBuilder(e.Args).Build();
            this._host.Start();

            this._trayIcon = new TrayIcon(this._host);
            this._trayIcon.Show();

            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            this._trayIcon?.Dispose();
            this._host?.Dispose();
            base.OnExit(e);
        }

        private static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args).ConfigureServices(services => { services.AddHostedService(_ => new WebApiHostedService(args)); });
        }
    }
}