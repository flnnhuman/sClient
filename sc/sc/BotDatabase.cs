using System;
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace sc
{
    public class BotDatabase : SerializableFile
    {
        
        internal string LoginKey {
            get => BackingLoginKey;

            set {
                if (BackingLoginKey == value) {
                    return;
                }

                BackingLoginKey = value;
                Utilities.InBackground(Save);
            }
        }
        [JsonProperty(PropertyName = "_LoginKey")]
        private string BackingLoginKey;

       //  [JsonProperty(PropertyName = "_MobileAuthenticator")]
       // private MobileAuthenticator BackingMobileAuthenticator;
       // 
       // internal MobileAuthenticator MobileAuthenticator {
       //     get => BackingMobileAuthenticator;

       //     set {
       //         if (BackingMobileAuthenticator == value) {
       //             return;
       //         }

       //         BackingMobileAuthenticator = value;
       //         Utilities.InBackground(Save);
       //     }
       // }
        
        
        
        
        private BotDatabase([NotNull] string filePath) {
            if (string.IsNullOrEmpty(filePath)) {
                throw new ArgumentNullException(nameof(filePath));
            }

            FilePath = filePath;
        }
  
    }
}