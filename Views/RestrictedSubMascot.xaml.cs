using System.Windows.Controls;

namespace EduGuardAgent.Views;

public partial class RestrictedSubMascot : UserControl
{
    public RestrictedSubMascot()
    {
        InitializeComponent();
        MascotImageLoader.Apply(PngImage, VectorFallback, "restricted-sub.png");
    }
}
