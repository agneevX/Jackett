﻿using Jackett.Common.Models.Config;
using Jackett.Common.Services.Interfaces;
using Jackett.Common.Utils;
using Jackett.Server.Services;
using NLog;
using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace Jackett.Server
{
    public static class Initialisation
    {
        public static void ProcessSettings(RuntimeSettings runtimeSettings, Logger logger)
        {
            if (runtimeSettings.ClientOverride != "httpclient" && runtimeSettings.ClientOverride != "httpclient2")
            {
                logger.Error($"Client override ({runtimeSettings.ClientOverride}) has been deprecated, please remove it from your start arguments");
                Environment.Exit(1);
            }

            if (runtimeSettings.DoSSLFix != null)
            {
                logger.Error("SSLFix has been deprecated, please remove it from your start arguments");
                Environment.Exit(1);
            }

            if (runtimeSettings.LogRequests)
            {
                logger.Info("Logging enabled.");
            }

            if (runtimeSettings.TracingEnabled)
            {
                logger.Info("Tracing enabled.");
            }

            if (runtimeSettings.IgnoreSslErrors == true)
            {
                logger.Info("Jackett will ignore SSL certificate errors.");
            }

            if (!string.IsNullOrWhiteSpace(runtimeSettings.CustomDataFolder))
            {
                logger.Info("Jackett Data will be stored in: " + runtimeSettings.CustomDataFolder);
            }

            if (runtimeSettings.ProxyConnection != null)
            {
                logger.Info("Proxy enabled. " + runtimeSettings.ProxyConnection);
            }
        }

        public static void ProcessWindowsSpecificArgs(ConsoleOptions consoleOptions, IProcessService processService, ServerConfig serverConfig, Logger logger)
        {
            IServiceConfigService serviceConfigService = new ServiceConfigService();

            /*  ======     Actions    =====  */

            // Install service
            if (consoleOptions.Install)
            {
                logger.Info("Initiating Jackett service install");
                serviceConfigService.Install();
                Environment.Exit(1);
            }

            // Uninstall service
            if (consoleOptions.Uninstall)
            {
                logger.Info("Initiating Jackett service uninstall");
                ReserveUrls(processService, serverConfig, logger, doInstall: false);
                serviceConfigService.Uninstall();
                Environment.Exit(1);
            }

            // Start Service
            if (consoleOptions.StartService)
            {
                if (!serviceConfigService.ServiceRunning())
                {
                    logger.Info("Initiating Jackett service start");
                    serviceConfigService.Start();
                }
                Environment.Exit(1);
            }

            // Stop Service
            if (consoleOptions.StopService)
            {
                if (serviceConfigService.ServiceRunning())
                {
                    logger.Info("Initiating Jackett service stop");
                    serviceConfigService.Stop();
                }
                Environment.Exit(1);
            }

            // Reserve urls
            if (consoleOptions.ReserveUrls)
            {
                logger.Info("Initiating ReserveUrls");
                ReserveUrls(processService, serverConfig, logger, doInstall: true);
                Environment.Exit(1);
            }
        }

        public static void ProcessConsoleOverrides(ConsoleOptions consoleOptions, IProcessService processService, ServerConfig serverConfig, IConfigurationService configurationService, Logger logger)
        {
            // Override port
            if (consoleOptions.Port != 0)
            {
                Int32.TryParse(serverConfig.Port.ToString(), out Int32 configPort);

                if (configPort != consoleOptions.Port)
                {
                    logger.Info("Overriding port to " + consoleOptions.Port);
                    serverConfig.Port = consoleOptions.Port;
                    bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                    if (isWindows)
                    {
                        if (ServerUtil.IsUserAdministrator())
                        {
                            ReserveUrls(processService, serverConfig, logger, doInstall: true);
                        }
                        else
                        {
                            logger.Error("Unable to switch ports when not running as administrator");
                            Environment.Exit(1);
                        }
                    }
                    configurationService.SaveConfig(serverConfig);
                }
            }

            // Override listen public
            if (consoleOptions.ListenPublic || consoleOptions.ListenPrivate)
            {
                if (serverConfig.AllowExternal != consoleOptions.ListenPublic)
                {
                    logger.Info("Overriding external access to " + consoleOptions.ListenPublic);
                    serverConfig.AllowExternal = consoleOptions.ListenPublic;
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        if (ServerUtil.IsUserAdministrator())
                        {
                            ReserveUrls(processService, serverConfig, logger, doInstall: true);
                        }
                        else
                        {
                            logger.Error("Unable to switch to public listening without admin rights.");
                            Environment.Exit(1);
                        }
                    }
                    configurationService.SaveConfig(serverConfig);
                }
            }
        }

        public static void ReserveUrls(IProcessService processService, ServerConfig serverConfig, Logger logger, bool doInstall = true)
        {
            logger.Debug("Unreserving Urls");
            serverConfig.GetListenAddresses(false).ToList().ForEach(u => RunNetSh(processService, string.Format("http delete urlacl {0}", u)));
            serverConfig.GetListenAddresses(true).ToList().ForEach(u => RunNetSh(processService, string.Format("http delete urlacl {0}", u)));
            if (doInstall)
            {
                logger.Debug("Reserving Urls");
                serverConfig.GetListenAddresses(serverConfig.AllowExternal).ToList().ForEach(u => RunNetSh(processService, string.Format("http add urlacl {0} sddl=D:(A;;GX;;;S-1-1-0)", u)));
                logger.Debug("Urls reserved");
            }
        }

        private static void RunNetSh(IProcessService processService, string args)
        {
            processService.StartProcessAndLog("netsh.exe", args);
        }
    }
}
