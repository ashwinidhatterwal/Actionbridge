using System.Diagnostics;
using System.Drawing.Printing;
using ActionBridge.Core;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
namespace ActionBridge.Windows;
public sealed class WindowsActions : IActionSink {
    readonly Control ui;
    public WindowsActions(Control ui) => this.ui=ui;
    public IReadOnlyList<PrintChoice> Printers() {
        var result=new List<PrintChoice>();
        foreach(string name in PrinterSettings.InstalledPrinters) {
            try {var settings=new PrinterSettings{PrinterName=name}; result.Add(new(name,settings.PaperSizes.Cast<PaperSize>().Select(p=>p.PaperName).Distinct().ToArray(),settings.SupportsColor,settings.CanDuplex,settings.IsDefaultPrinter));}catch(InvalidPrinterException) { }
        }
        return result;
    }
    Task OnUi(Action action) {
        var t=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.BeginInvoke(()=>{try{action();t.SetResult();}catch(Exception e){t.SetException(e);}});return t.Task;
    }
    public async Task ExecuteAsync(JobRequest r,string? file) {
        switch(r.Action) {
            case "save": return;
            case "copy": await OnUi(()=>Clipboard.SetText(r.Text!)); return;
            case "url": await OnUi(()=>Process.Start(new ProcessStartInfo(r.Text!){UseShellExecute=true}));return;
            case "open":
                var ext=Path.GetExtension(file!).ToLowerInvariant();
                if(!new[]{".pdf",".jpg",".jpeg",".png",".bmp",".gif",".webp",".txt",".csv",".docx",".xlsx",".pptx",".odt",".ods",".odp",".mp3",".wav",".m4a",".mp4",".mkv",".mov"}.Contains(ext)) throw new InvalidOperationException("File saved. Automatic opening is disabled for this file type.");
                await OnUi(()=>Process.Start(new ProcessStartInfo(file!){UseShellExecute=true}));return;
            case "print": await Task.Run(()=>Print(r,file!));return;
        }
    }
    static void Print(JobRequest r,string path) {
        using var doc=new PrintDocument();doc.PrinterSettings.PrinterName=r.Printer??new PrinterSettings().PrinterName;
        if(!doc.PrinterSettings.IsValid) throw new InvalidPrinterException(doc.PrinterSettings);
        doc.DocumentName=Wire.SafeName(r.Name);doc.PrinterSettings.Copies=(short)r.Copies;
        doc.PrinterSettings.Collate=r.Collate;
        if(r.Duplex!="default") {
            if(r.Duplex!="simplex"&&!doc.PrinterSettings.CanDuplex)throw new InvalidOperationException("This printer does not support two-sided printing.");
            doc.PrinterSettings.Duplex=r.Duplex switch {"long"=>Duplex.Vertical,"short"=>Duplex.Horizontal,_=>Duplex.Simplex};
        }
        doc.DefaultPageSettings.Landscape=r.Landscape;doc.DefaultPageSettings.Color=r.Color&&doc.PrinterSettings.SupportsColor;
        if(r.Paper!="Default") {
            var paper=doc.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(p=>p.PaperName==r.Paper)??throw new InvalidOperationException("Selected paper size is unavailable.");
            doc.DefaultPageSettings.PaperSize=paper;
        }
        doc.DefaultPageSettings.Margins=new Margins(15,15,15,15);
        doc.PrintController=new StandardPrintController();
        PdfDocument? pdf=null; Image? image=null; uint index=0;uint endPage=0;
        try {
            if(Path.GetExtension(path).Equals(".pdf",StringComparison.OrdinalIgnoreCase)) {
                var source=StorageFile.GetFileFromPathAsync(path).AsTask().GetAwaiter().GetResult();
                pdf=PdfDocument.LoadFromFileAsync(source).AsTask().GetAwaiter().GetResult();
                if(pdf.PageCount==0 || pdf.PageCount>500) throw new InvalidOperationException("Print supports 1–500 PDF pages per job.");
                            if(r.PageFrom>0 && r.PageTo>pdf.PageCount)throw new InvalidOperationException($"PDF has {pdf.PageCount} pages. Choose a valid range.");
                index=r.PageFrom>0?(uint)r.PageFrom-1:0;endPage=r.PageTo>0?(uint)r.PageTo:pdf.PageCount;
            } else {
                image=Image.FromFile(path);
                if((long)image.Width*image.Height>80_000_000) throw new InvalidOperationException("Image exceeds 80 megapixels. Resize it before printing.");
            }
            doc.PrintPage+=(s,e)=> {
                Image current; bool dispose=false;
                if(pdf!=null) {
                    using var page=pdf.GetPage(index); using var stream=new InMemoryRandomAccessStream();
                    // Render at up to 300 dpi, capped to avoid huge allocations.
                    var raster=PrintGeometry.PdfRaster(page.Size.Width,page.Size.Height);
                    page.RenderToStreamAsync(stream,new PdfPageRenderOptions{DestinationWidth=raster.Width,DestinationHeight=raster.Height}).AsTask().GetAwaiter().GetResult();
                    using var managed=stream.AsStreamForRead();using var loaded=Image.FromStream(managed);var rendered=new Bitmap(loaded);rendered.SetResolution(raster.Dpi,raster.Dpi);current=rendered;dispose=true;
                } else current=image!;
                try {
                    var bounds=e.MarginBounds;var scaleX=bounds.Width/(float)current.Width;var scaleY=bounds.Height/(float)current.Height;
                    var scale=r.Fit=="fill"?Math.Max(scaleX,scaleY):Math.Min(scaleX,scaleY);
                    if(r.Fit=="actual") scale=100f/Math.Max(1,current.HorizontalResolution);
                    var w=current.Width*scale;var h=current.Height*scale;
                    var g=e.Graphics!;g.TranslateTransform(-e.PageSettings.HardMarginX,-e.PageSettings.HardMarginY);
                    g.SetClip(bounds);g.DrawImage(current,bounds.Left+(bounds.Width-w)/2,bounds.Top+(bounds.Height-h)/2,w,h);
                    index++;e.HasMorePages=pdf!=null&&index<endPage;
                } finally {if(dispose) current.Dispose();}
            };
            doc.Print();
        } finally {image?.Dispose();}
    }
}
