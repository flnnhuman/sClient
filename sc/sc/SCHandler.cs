using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using JetBrains.Annotations;
using SteamKit2;
using SteamKit2.Internal;
using SteamKit2.Unified.Internal;

namespace sc
{
    public class SCHandler: ClientMsgHandler
    {
        private static readonly Logger Logger;
        private readonly SteamUnifiedMessages.UnifiedService<IChatRoom> UnifiedChatRoomService;
        private readonly SteamUnifiedMessages.UnifiedService<IClanChatRooms> UnifiedClanChatRoomsService;
        private readonly SteamUnifiedMessages.UnifiedService<IEcon> UnifiedEconService;
        private readonly SteamUnifiedMessages.UnifiedService<IFriendMessages> UnifiedFriendMessagesService;
        private readonly SteamUnifiedMessages.UnifiedService<IPlayer> UnifiedPlayerService;
        
        internal DateTime LastPacketReceived { get; private set; }
        
        internal SCHandler([NotNull] Logger Logger, [NotNull] SteamUnifiedMessages steamUnifiedMessages) {
            if ((Logger == null) || (steamUnifiedMessages == null)) {
                throw new ArgumentNullException(nameof(Logger) + " || " + nameof(steamUnifiedMessages));
            }

            Logger = Logger;
            UnifiedChatRoomService = steamUnifiedMessages.CreateService<IChatRoom>();
            UnifiedClanChatRoomsService = steamUnifiedMessages.CreateService<IClanChatRooms>();
            UnifiedEconService = steamUnifiedMessages.CreateService<IEcon>();
            UnifiedFriendMessagesService = steamUnifiedMessages.CreateService<IFriendMessages>();
            UnifiedPlayerService = steamUnifiedMessages.CreateService<IPlayer>();
        }
        
        public override void HandleMsg(IPacketMsg packetMsg) {
            if (packetMsg == null) {
                Logger.LogNullError(nameof(packetMsg));

                return;
            }

            LastPacketReceived = DateTime.UtcNow;

            switch (packetMsg.MsgType) {
                case EMsg.ClientItemAnnouncements:
                    HandleItemAnnouncements(packetMsg);

                    break;
                case EMsg.ClientPlayingSessionState:
                    HandlePlayingSessionState(packetMsg);

                    break;
                case EMsg.ClientPurchaseResponse:
                    HandlePurchaseResponse(packetMsg);

                    break;
                case EMsg.ClientRedeemGuestPassResponse:
                    HandleRedeemGuestPassResponse(packetMsg);

                    break;
                case EMsg.ClientSharedLibraryLockStatus:
                    HandleSharedLibraryLockStatus(packetMsg);

                    break;
                case EMsg.ClientUserNotifications:
                    HandleUserNotifications(packetMsg);

                    break;
                case EMsg.ClientVanityURLChangedNotification:
                    HandleVanityURLChangedNotification(packetMsg);

                    break;
            }
        }
        	internal sealed class UserNotificationsCallback : CallbackMsg {
			internal readonly Dictionary<EUserNotification, uint> Notifications;

			internal UserNotificationsCallback([NotNull] JobID jobID, [NotNull] CMsgClientUserNotifications msg) {
				if ((jobID == null) || (msg == null)) {
					throw new ArgumentNullException(nameof(jobID) + " || " + nameof(msg));
				}

				JobID = jobID;

				// We might get null body here, and that means there are no notifications related to trading
				Notifications = new Dictionary<EUserNotification, uint> { { EUserNotification.Trading, 0 } };

				if (msg.notifications == null) {
					return;
				}

				foreach (CMsgClientUserNotifications.Notification notification in msg.notifications) {
					EUserNotification type = (EUserNotification) notification.user_notification_type;

					switch (type) {
						case EUserNotification.AccountAlerts:
						case EUserNotification.Chat:
						case EUserNotification.Comments:
						case EUserNotification.GameTurns:
						case EUserNotification.Gifts:
						case EUserNotification.HelpRequestReplies:
						case EUserNotification.Invites:
						case EUserNotification.Items:
						case EUserNotification.ModeratorMessages:
						case EUserNotification.Trading:
							break;
						default:
							Logger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(type), type));

							continue;
					}

					Notifications[type] = notification.count;
				}
			}

			internal UserNotificationsCallback([NotNull] JobID jobID, [NotNull] CMsgClientItemAnnouncements msg) {
				if ((jobID == null) || (msg == null)) {
					throw new ArgumentNullException(nameof(jobID) + " || " + nameof(msg));
				}

				JobID = jobID;
				Notifications = new Dictionary<EUserNotification, uint>(1) { { EUserNotification.Items, msg.count_new_items } };
			}

			[PublicAPI]
			internal enum EUserNotification : byte {
				Unknown,
				Trading,
				GameTurns,
				ModeratorMessages,
				Comments,
				Items,
				Invites,
				Unknown7, // No clue what 7 stands for, and I doubt we can find out
				Gifts,
				Chat,
				HelpRequestReplies,
				AccountAlerts
			}
		}
            internal sealed class PlayingSessionStateCallback : CallbackMsg {
	            internal readonly bool PlayingBlocked;

	            internal PlayingSessionStateCallback([NotNull] JobID jobID, [NotNull] CMsgClientPlayingSessionState msg) {
		            if ((jobID == null) || (msg == null)) {
			            throw new ArgumentNullException(nameof(jobID) + " || " + nameof(msg));
		            }

		            JobID = jobID;
		            PlayingBlocked = msg.playing_blocked;
	            }
            }
            public sealed class PurchaseResponseCallback : CallbackMsg {
	            public readonly Dictionary<uint, string> Items;

	            public EPurchaseResultDetail PurchaseResultDetail { get; internal set; }
	            public EResult Result { get; internal set; }

	            internal PurchaseResponseCallback([NotNull] JobID jobID, [NotNull] CMsgClientPurchaseResponse msg) {
		            if ((jobID == null) || (msg == null)) {
			            throw new ArgumentNullException(nameof(jobID) + " || " + nameof(msg));
		            }

		            JobID = jobID;
		            PurchaseResultDetail = (EPurchaseResultDetail) msg.purchase_result_details;
		            Result = (EResult) msg.eresult;

		            if (msg.purchase_receipt_info == null) {
			            Logger.LogNullError(nameof(msg.purchase_receipt_info));

			            return;
		            }

		            KeyValue receiptInfo = new KeyValue();

		            using (MemoryStream ms = new MemoryStream(msg.purchase_receipt_info)) {
			            if (!receiptInfo.TryReadAsBinary(ms)) {
				            Logger.LogNullError(nameof(ms));

				            return;
			            }
		            }

		            List<KeyValue> lineItems = receiptInfo["lineitems"].Children;

		            if (lineItems.Count == 0) {
			            return;
		            }

		            Items = new Dictionary<uint, string>(lineItems.Count);

		            foreach (KeyValue lineItem in lineItems) {
			            uint packageID = lineItem["PackageID"].AsUnsignedInteger();

			            if (packageID == 0) {
				            // Coupons have PackageID of -1 (don't ask me why)
				            // We'll use ItemAppID in this case
				            packageID = lineItem["ItemAppID"].AsUnsignedInteger();

				            if (packageID == 0) {
					            Logger.LogNullError(nameof(packageID));

					            return;
				            }
			            }

			            string gameName = lineItem["ItemDescription"].Value;

			            if (string.IsNullOrEmpty(gameName)) {
				            Logger.LogNullError(nameof(gameName));

				            return;
			            }

			            // Apparently steam expects client to decode sent HTML
			            gameName = WebUtility.HtmlDecode(gameName);
			            Items[packageID] = gameName;
		            }
	            }
            }
            internal sealed class RedeemGuestPassResponseCallback : CallbackMsg {
	            internal readonly EResult Result;

	            internal RedeemGuestPassResponseCallback([NotNull] JobID jobID, [NotNull] CMsgClientRedeemGuestPassResponse msg) {
		            if ((jobID == null) || (msg == null)) {
			            throw new ArgumentNullException(nameof(jobID) + " || " + nameof(msg));
		            }

		            JobID = jobID;
		            Result = (EResult) msg.eresult;
	            }
            }
            internal sealed class SharedLibraryLockStatusCallback : CallbackMsg {
	            internal readonly ulong LibraryLockedBySteamID;

	            internal SharedLibraryLockStatusCallback([NotNull] JobID jobID, [NotNull] CMsgClientSharedLibraryLockStatus msg) {
		            if ((jobID == null) || (msg == null)) {
			            throw new ArgumentNullException(nameof(jobID) + " || " + nameof(msg));
		            }

		            JobID = jobID;

		            if (msg.own_library_locked_by == 0) {
			            return;
		            }

		            LibraryLockedBySteamID = new SteamID(msg.own_library_locked_by, EUniverse.Public, EAccountType.Individual);
	            }
            }
            internal sealed class VanityURLChangedCallback : CallbackMsg {
	            internal readonly string VanityURL;

	            internal VanityURLChangedCallback([NotNull] JobID jobID, [NotNull] CMsgClientVanityURLChangedNotification msg) {
		            if ((jobID == null) || (msg == null)) {
			            throw new ArgumentNullException(nameof(jobID) + " || " + nameof(msg));
		            }

		            JobID = jobID;
		            VanityURL = msg.vanity_url;
	            }
            }
            
        private void HandleItemAnnouncements(IPacketMsg packetMsg) {
            if (packetMsg == null) {
                Logger.LogNullError(nameof(packetMsg));

                return;
            }

            ClientMsgProtobuf<CMsgClientItemAnnouncements> response = new ClientMsgProtobuf<CMsgClientItemAnnouncements>(packetMsg);
            Client.PostCallback(new UserNotificationsCallback(packetMsg.TargetJobID, response.Body));
        }
        private void HandlePlayingSessionState(IPacketMsg packetMsg) {
	        if (packetMsg == null) {
		        Logger.LogNullError(nameof(packetMsg));

		        return;
	        }

	        ClientMsgProtobuf<CMsgClientPlayingSessionState> response = new ClientMsgProtobuf<CMsgClientPlayingSessionState>(packetMsg);
	        Client.PostCallback(new PlayingSessionStateCallback(packetMsg.TargetJobID, response.Body));
        }
        private void HandlePurchaseResponse(IPacketMsg packetMsg) {
	        if (packetMsg == null) {
		        Logger.LogNullError(nameof(packetMsg));

		        return;
	        }

	        ClientMsgProtobuf<CMsgClientPurchaseResponse> response = new ClientMsgProtobuf<CMsgClientPurchaseResponse>(packetMsg);
	        Client.PostCallback(new PurchaseResponseCallback(packetMsg.TargetJobID, response.Body));
        }
        private void HandleRedeemGuestPassResponse(IPacketMsg packetMsg) {
	        if (packetMsg == null) {
		        Logger.LogNullError(nameof(packetMsg));

		        return;
	        }

	        ClientMsgProtobuf<CMsgClientRedeemGuestPassResponse> response = new ClientMsgProtobuf<CMsgClientRedeemGuestPassResponse>(packetMsg);
	        Client.PostCallback(new RedeemGuestPassResponseCallback(packetMsg.TargetJobID, response.Body));
        }
        private void HandleSharedLibraryLockStatus(IPacketMsg packetMsg) {
	        if (packetMsg == null) {
		        Logger.LogNullError(nameof(packetMsg));

		        return;
	        }

	        ClientMsgProtobuf<CMsgClientSharedLibraryLockStatus> response = new ClientMsgProtobuf<CMsgClientSharedLibraryLockStatus>(packetMsg);
	        Client.PostCallback(new SharedLibraryLockStatusCallback(packetMsg.TargetJobID, response.Body));
        }
        private void HandleUserNotifications(IPacketMsg packetMsg) {
	        if (packetMsg == null) {
		        Logger.LogNullError(nameof(packetMsg));

		        return;
	        }

	        ClientMsgProtobuf<CMsgClientUserNotifications> response = new ClientMsgProtobuf<CMsgClientUserNotifications>(packetMsg);
	        Client.PostCallback(new UserNotificationsCallback(packetMsg.TargetJobID, response.Body));
        }
        private void HandleVanityURLChangedNotification(IPacketMsg packetMsg) {
	        if (packetMsg == null) {
		        Logger.LogNullError(nameof(packetMsg));

		        return;
	        }

	        ClientMsgProtobuf<CMsgClientVanityURLChangedNotification> response = new ClientMsgProtobuf<CMsgClientVanityURLChangedNotification>(packetMsg);
	        Client.PostCallback(new VanityURLChangedCallback(packetMsg.TargetJobID, response.Body));
        }
    }
}