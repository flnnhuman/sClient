using System;

namespace sc
{
    public class BotConfig
    {
        private const bool DefaultUseLoginKeys = true;
        public readonly bool UseLoginKeys = DefaultUseLoginKeys;
        private const string DefaultSteamLogin = null;
        private const string DefaultSteamPassword = null;
        
        public string SteamLogin = DefaultSteamLogin;
        public string SteamPassword = DefaultSteamPassword;

        public string DecryptedSteamPassword = "";
        


    }
}