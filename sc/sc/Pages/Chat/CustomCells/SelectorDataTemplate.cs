using System;
using sc.Chat.Model;
using Xamarin.Forms;

namespace sc.Chat.CustomCells
{
    public class SelectorDataTemplate : DataTemplateSelector
    {
        private readonly DataTemplate textInDataTemplate;
        private readonly DataTemplate textOutDataTemplate;
        private readonly DataTemplate imageInDataTemplate;
        protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        {
            var messageVm = item as Message;
            if (messageVm == null)
                return null;


            bool result = Uri.IsWellFormedUriString(messageVm.Text, UriKind.Absolute);
            
            switch (messageVm.IsTextIn)
            {
                case true when result ==true: return imageInDataTemplate;
                case true: return this.textInDataTemplate;
                case false: return this.textOutDataTemplate;
            }
            
           
        }


        public SelectorDataTemplate()
        {
            this.textInDataTemplate = new DataTemplate(typeof(TextInViewCell));
            this.textOutDataTemplate = new DataTemplate(typeof(TextOutViewCell));
            this.imageInDataTemplate = new DataTemplate(typeof(ImageInViewCell)); 
        }

    }
}
