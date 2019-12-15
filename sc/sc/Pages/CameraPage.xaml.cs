using System;
using System.IO;
using Plugin.Media;
using Plugin.Media.Abstractions;
using Plugin.Toast;
using Plugin.Toast.Abstractions;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;


namespace sc
{
  [XamlCompilation(XamlCompilationOptions.Compile)]
  public partial class CameraPage : ContentPage
  {
    private string AvatarPath;
    public CameraPage()
    {
      InitializeComponent();
    }

    private async void TakePhoto_OnClicked(object sender, EventArgs e)
      {
        {

          if (!CrossMedia.Current.IsCameraAvailable || !CrossMedia.Current.IsTakePhotoSupported)
          {
            DisplayAlert("No Camera", ":( No camera available.", "OK");
            return;
          }

          var file = await CrossMedia.Current.TakePhotoAsync(new Plugin.Media.Abstractions.StoreCameraMediaOptions
          {
            Directory = "Test",
            SaveToAlbum = true,
            CompressionQuality = 75,
            CustomPhotoSize = 50,
            PhotoSize = PhotoSize.MaxWidthHeight,
            MaxWidthHeight = 2000,
            DefaultCamera = CameraDevice.Front
          });

          if (file == null)
            return;

          DisplayAlert("File Location", file.Path, "OK");
          AvatarPath = file.Path;
          image.Source = ImageSource.FromStream(() =>
          {
            var stream = file.GetStream();
            file.Dispose();
            return stream;
          });
        }
        ;
      }

      private async void PickPhoto_OnClicked(object sender, EventArgs e)
      {

        if (!CrossMedia.Current.IsPickPhotoSupported)
        {
          DisplayAlert("Photos Not Supported", ":( Permission not granted to photos.", "OK");
          return;
        }

        var file = await Plugin.Media.CrossMedia.Current.PickPhotoAsync(new Plugin.Media.Abstractions.PickMediaOptions
        {
          PhotoSize = Plugin.Media.Abstractions.PhotoSize.Medium,

        });


        if (file == null)
          return;
        AvatarPath = file.Path;
        image.Source = ImageSource.FromStream(() =>
        {
          var stream = file.GetStream();
          file.Dispose();
          return stream;
        });
      }

      private async void Avatar_OnClicked(object sender, EventArgs e)
      {

        var result = await sc.bot.WebHandler.UploadAvatar(AvatarPath, sc.bot.SteamID);
        if (File.Exists(AvatarPath))
        {
          CrossToastPopUp.Current.ShowToastMessage(result, ToastLength.Short);
        }

        if (result == "true")
        {
          sc.bot.RequestPersonaStateUpdate();
        }
      }
  }
}