using System.Windows.Controls;

namespace EduGuardAgent.Views;

public partial class TrustedSubMascot : UserControl
{
    public TrustedSubMascot()
    {
        InitializeComponent();
        MascotImageLoader.Apply(PngImage, VectorFallback, "trusted-sub.png");
    }
}
