using System;
using Plugin.Toast;
using Xamarin.Forms;

namespace sc {
	public partial class MainPage : ContentPage {
		public enum EToastType {
			Message,
			Warning,
			Error,
			Success
		}

		public MainPage() {
			InitializeComponent();
		}


		private async void Button_OnClicked(object sender, EventArgs e) {
		}

		private void Button2_OnClicked(object sender, EventArgs e) {
		}

		private async void Button3_OnClicked(object sender, EventArgs e) {
		}

		public static void Toast(string msg, EToastType type) {
			Device.BeginInvokeOnMainThread(() => {
				switch (type) {
					case EToastType.Message:
						CrossToastPopUp.Current.ShowToastMessage(msg);
						break;
					case EToastType.Error:
						CrossToastPopUp.Current.ShowToastError(msg);
						break;
					case EToastType.Warning:
						CrossToastPopUp.Current.ShowToastWarning(msg);
						break;
					case EToastType.Success:
						CrossToastPopUp.Current.ShowToastSuccess(msg);
						break;
				}
			});
		}
	}
}
