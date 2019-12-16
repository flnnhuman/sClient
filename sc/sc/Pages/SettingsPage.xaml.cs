using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace sc
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class SettingsPage : ContentPage
    {
        public SettingsPage()
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(sc.GlobalConfig.Language);
            InitializeComponent();
            
            Debug.BindingContext =sc.GlobalConfig;
            ConnectionTimeout.BindingContext = sc.GlobalConfig;
            LoginLimiterDelay.BindingContext = sc.GlobalConfig;
            WebLimiterDelay.BindingContext = sc.GlobalConfig;
            SteamProtocols.BindingContext = sc.GlobalConfig;
            OnlineStatus.BindingContext = sc.bot.BotConfig;
        }
        void IsDebug(object sender, EventArgs eventArgs)
        {
           
            sc.GlobalConfig.Debug = Debug.On;
            
            sc.GlobalConfig.Write(Path.Combine(Bot.MainDir,"config.json"));
            Debug.On = sc.GlobalConfig.Debug;
        }
        void ConnectionTimeoutCompleted(object sender, EventArgs eventArgs)
        {
            int result=0;
            bool isValid = Int32.TryParse(ConnectionTimeout.Text,out result);
            if (isValid)
            {
                sc.GlobalConfig.ConnectionTimeout = Byte.Parse(result.ToString());
            }
            sc.GlobalConfig.Write(Path.Combine(Bot.MainDir,"config.json"));
            ConnectionTimeout.Text = sc.GlobalConfig.ConnectionTimeout.ToString();
        }
        void LoginLimiterDelayCompleted(object sender, EventArgs eventArgs)
        {
            int result=0;
            bool isValid = Int32.TryParse(LoginLimiterDelay.Text,out result);
            if (isValid)
            {
                sc.GlobalConfig.LoginLimiterDelay = Byte.Parse(result.ToString());
            }
            sc.GlobalConfig.Write(Path.Combine(Bot.MainDir,"config.json"));
            LoginLimiterDelay.Text = sc.GlobalConfig.LoginLimiterDelay.ToString();
        }
        void WebLimiterDelayCompleted(object sender, EventArgs eventArgs)
        {
            int result=0;
            bool isValid = Int32.TryParse(WebLimiterDelay.Text,out result);
            if (isValid)
            {
                sc.GlobalConfig.WebLimiterDelay = Byte.Parse(result.ToString());
            }
            sc.GlobalConfig.Write(Path.Combine(Bot.MainDir,"config.json"));
            WebLimiterDelay.Text = sc.GlobalConfig.WebLimiterDelay.ToString();
        }
        void SteamProtocolsCompleted(object sender, EventArgs eventArgs)
        {
            int result=0;
            bool isValid = Int32.TryParse(SteamProtocols.Text,out result);
            if (isValid &&(result==1|| result==2 || result==4||result==7))
            {
                sc.GlobalConfig.SteamProtocols = (ProtocolTypes)result;
            }
            sc.GlobalConfig.Write(Path.Combine(Bot.MainDir,"config.json"));
            SteamProtocols.Text = "";
            SteamProtocols.Placeholder = sc.GlobalConfig.SteamProtocols.ToString();
        }
        
        void OnlineStatusCompleted(object sender, EventArgs eventArgs)
        {
            
             if (OnlineStatus.On)
             {
                 sc.bot.BotConfig.OnlineStatus = (EPersonaState) 1;
             }
             else sc.bot.BotConfig.OnlineStatus = (EPersonaState) 0;
            
            sc.bot.BotConfig.Write(Path.Combine(Bot.MainDir,SharedInfo.ConfigDirectory,$"{sc.bot.BotName}.json"));
          
        }
        
        
    }
}