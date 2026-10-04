namespace ActionBridge.Core;
public static class PrintGeometry {
    public static (uint Width,uint Height,float Dpi) PdfRaster(double width,double height) {
        if(!double.IsFinite(width)||!double.IsFinite(height)||width<=0||height<=0)throw new ArgumentException("PDF page dimensions are invalid.");
        var scale=Math.Min(300d/96,5000d/Math.Max(width,height));
        var w=(uint)Math.Clamp(Math.Round(width*scale),1,5000);var h=(uint)Math.Clamp(Math.Round(height*scale),1,5000);
        return (w,h,(float)(w*96d/width));
    }
}
