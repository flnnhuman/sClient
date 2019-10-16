using System.Threading.Tasks;

namespace sc
{
    internal static class Events
    {
        private static Logger Logger;
        internal static async Task OnBotShutdown() {
            /*
            if (Program.ProcessRequired || Bot.Bots.Values.Any(bot => bot.KeepRunning)) {
                return;
            }

            Logger.LogGenericInfo(Strings.NoBotsAreRunning);
                                                      
            // We give user extra 5 seconds for eventual config changes
            await Task.Delay(5000).ConfigureAwait(false);

            if (Program.ProcessRequired || Bot.Bots.Values.Any(bot => bot.KeepRunning)) {
                return;
            }

            await Program.Exit().ConfigureAwait(false);
        
        */
           //  TODO наверно ебнуть тут открытие формы ввода login:pass 
        }   
    }
}