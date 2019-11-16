using System;
using System.Reflection;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace sc
{
    public class SharedInfo
    {
        internal const string AssemblyName = "SClient";
        internal const string GithubRepo = "flnnhuman/" + AssemblyName;
        internal const string ProjectURL = "https://github.com/" + GithubRepo;
        internal const string SentryHashExtension = ".bin";
        internal const string MobileAuthenticatorExtension = ".maFile";
        internal const string JsonConfigExtension = ".json";
        internal const string KeysExtension = ".keys";
        internal const string KeysUnusedExtension = ".unused";
        internal const string DatabaseExtension = ".db";
        internal const string ConfigDirectory = "config";
        internal const string DebugDirectory = "debug";
        internal const string SC = nameof(sc);
        internal const string GlobalConfigFileName = SC + JsonConfigExtension;
        internal const string GlobalDatabaseFileName = SC + DatabaseExtension;

        internal static Version Version =>Version.Parse(AppInfo.VersionString);
        
        internal static string PublicIdentifier => AssemblyName;

    }
}