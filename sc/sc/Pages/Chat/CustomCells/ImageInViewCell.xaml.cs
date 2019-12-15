using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace sc.Chat.CustomCells
{
 
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ImageInViewCell : ViewCell
    {
        public ImageInViewCell()
        {
            InitializeComponent();
        }
    }
}