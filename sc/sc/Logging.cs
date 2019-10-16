using System;
using System.Threading.Tasks;

namespace sc
{
    public class Logging
    {
	    private static Logger Logger;
        	internal static async Task<string> GetUserInput(sc.EUserInputType userInputType, string botName = SharedInfo.ASF) {
			if (userInputType == sc.EUserInputType.Unknown) {
				return null;
			}

			//	if (GlobalConfig.Headless) {
			//		ArchiLogger.LogGenericWarning(Strings.ErrorUserInputRunningInHeadlessMode);
			//
			//		return null;
			//	}

			await ConsoleSemaphore.WaitAsync().ConfigureAwait(false);
			
			string result;

			try {
				OnUserInputStart();

				try {
					Console.Beep();

					switch (userInputType) {
						case sc.EUserInputType.DeviceID:
							Console.Write(Bot.FormatBotResponse(Strings.UserInputDeviceID, botName));
							result = ConsoleReadLine();

							break;
						case sc.EUserInputType.Login:
							Console.Write(Bot.FormatBotResponse(Strings.UserInputSteamLogin, botName));
							result = ConsoleReadLine();

							break;
						case sc.EUserInputType.Password:
							Console.Write(Bot.FormatBotResponse(Strings.UserInputSteamPassword, botName));
							result = ConsoleReadLineMasked();

							break;
						case sc.EUserInputType.SteamGuard:
							Console.Write(Bot.FormatBotResponse(Strings.UserInputSteamGuard, botName));
							result = ConsoleReadLine();

							break;
						case sc.EUserInputType.SteamParentalCode:
							Console.Write(Bot.FormatBotResponse(Strings.UserInputSteamParentalCode, botName));
							result = ConsoleReadLineMasked();

							break;
						case sc.EUserInputType.TwoFactorAuthentication:
							Console.Write(Bot.FormatBotResponse(Strings.UserInputSteam2FA, botName));
							result = ConsoleReadLine();

							break;
						default:
							Logger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(userInputType), userInputType));
							Console.Write(Bot.FormatBotResponse(string.Format(Strings.UserInputUnknown, userInputType), botName));
							result = ConsoleReadLine();

							break;
					}

					if (!Console.IsOutputRedirected) {
						Console.Clear(); // For security purposes
					}
				} catch (Exception e) {
					OnUserInputEnd();
					Logger.LogGenericException(e);

					return null;
				}

				OnUserInputEnd();
			} finally {
				ConsoleSemaphore.Release();
			}

			return !string.IsNullOrEmpty(result) ? result.Trim() : null;
		}

    }
}