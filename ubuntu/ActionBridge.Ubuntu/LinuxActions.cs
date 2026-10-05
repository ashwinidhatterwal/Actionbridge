using System.Text.RegularExpressions;
using ActionBridge.Core;
namespace ActionBridge.Ubuntu;
public sealed class LinuxActions(ICommands commands,Func<string,object,Task<System.Text.Json.JsonElement>> desktop):IActionSink {
 public IReadOnlyList<PrintChoice> Printers()=>ListPrinters().GetAwaiter().GetResult();
 public async Task<IReadOnlyList<PrintChoice>> ListPrinters(){
  var listing=await commands.Run("/usr/bin/lpstat",new[]{"-p"});if(listing.Exit!=0)return Array.Empty<PrintChoice>();
  var def=await commands.Run("/usr/bin/lpstat",new[]{"-d"});var defaultName=def.Output.Trim().Split(':').Last().Trim();var result=new List<PrintChoice>();
  foreach(Match match in Regex.Matches(listing.Output,@"(?m)^printer ([^\s]+) ")){
   var name=match.Groups[1].Value;var opts=await commands.Run("/usr/bin/lpoptions",new[]{"-p",name,"-l"});var papers=Options(opts.Output,"PageSize");if(papers.Length==0)papers=Options(opts.Output,"media");
   var colors=Options(opts.Output,"ColorModel").Concat(Options(opts.Output,"print-color-mode")).ToArray();var duplex=Options(opts.Output,"Duplex").Concat(Options(opts.Output,"sides")).ToArray();
   result.Add(new(name,papers,colors.Any(x=>x.Contains("color",StringComparison.OrdinalIgnoreCase)||x is "RGB" or "CMYK"),duplex.Any(x=>x.Contains("two-sided")||x.Contains("Duplex")),name==defaultName));
  }return result;
 }
 public static string[] Options(string output,string key){var line=output.Split('\n').FirstOrDefault(x=>x.StartsWith(key+"/",StringComparison.Ordinal)||x.StartsWith(key+":",StringComparison.Ordinal));return line==null?[]:line[(line.IndexOf(':')+1)..].Split(' ',StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim().TrimStart('*')).Where(x=>x.Length>0).Distinct().ToArray();}
 public static string[] PrintArguments(JobRequest r,string file){
  var args=new List<string>{"-n",r.Copies.ToString(),"-t",Wire.SafeName(r.Name)};if(r.Printer!=null){args.Add("-d");args.Add(r.Printer);}void Option(string value){args.Add("-o");args.Add(value);}
  Option("orientation-requested="+(r.Landscape?"4":"3"));Option("print-color-mode="+(r.Color?"color":"monochrome"));Option("ColorModel="+(r.Color?"RGB":"Gray"));Option("Collate="+(r.Collate?"True":"False"));Option("print-scaling="+(r.Fit=="actual"?"none":r.Fit));
  if(r.Fit=="fit")Option("fit-to-page=true");else if(r.Fit=="actual")Option("fit-to-page=false");
  if(r.Paper!="Default")Option("media="+r.Paper);if(r.Duplex!="default")Option("sides="+(r.Duplex switch{"long"=>"two-sided-long-edge","short"=>"two-sided-short-edge",_=>"one-sided"}));
  if(r.PageFrom>0){args.Add("-P");args.Add(r.PageFrom+"-"+r.PageTo);}args.Add("--");args.Add(file);return args.ToArray();
 }
 public async Task ExecuteAsync(JobRequest r,string? file){
  switch(r.Action){
   case "save":return;
   case "copy":var response=await desktop("clipboard",new{text=r.Text});if(!response.GetProperty("copied").GetBoolean())throw new InvalidOperationException("Clipboard copy was not confirmed.");return;
   case "url":Wire.Validate(r);await Open(r.Text!);return;
   case "open":if(!AllowedOpen(file!))throw new InvalidOperationException("File saved. Automatic opening is disabled for this file type.");await Open(file!);return;
   case "print":await Print(r,file!);return;
   default:throw new ArgumentException("Unknown action.");
  }
 }
 public static bool AllowedOpen(string path)=>new[]{".pdf",".jpg",".jpeg",".png",".bmp",".gif",".webp",".txt",".csv",".docx",".xlsx",".pptx",".odt",".ods",".odp",".mp3",".wav",".m4a",".mp4",".mkv",".mov"}.Contains(Path.GetExtension(path).ToLowerInvariant());
 public async Task Open(string target){var r=await desktop("open",new{target});if(!r.GetProperty("opened").GetBoolean())throw new InvalidOperationException("Could not open this item. It remains saved; choose an installed application.");}
 async Task Print(JobRequest r,string file){
  Wire.Validate(r);var printers=await ListPrinters();var selected=r.Printer==null?printers.FirstOrDefault(x=>x.IsDefault):printers.FirstOrDefault(x=>x.Name==r.Printer);if(selected==null)throw new InvalidOperationException("No selected/default printer. Add a printer in Ubuntu Settings.");
  if(r.Paper!="Default"&&!selected.Papers.Contains(r.Paper))throw new InvalidOperationException("Selected paper size is unavailable.");if(r.Duplex is "long" or "short"&&!selected.SupportsDuplex)throw new InvalidOperationException("This printer does not advertise two-sided printing.");
  if(Path.GetExtension(file).Equals(".pdf",StringComparison.OrdinalIgnoreCase)){
   var info=await commands.Run("/usr/bin/pdfinfo",new[]{file});var match=Regex.Match(info.Output,@"(?m)^Pages:\s+(\d+)");if(info.Exit!=0||!match.Success||!int.TryParse(match.Groups[1].Value,out var pages)||pages is <1 or >500)throw new InvalidOperationException("Print supports valid PDFs with 1–500 pages.");if(r.PageTo>pages)throw new InvalidOperationException("Selected page range exceeds the PDF length.");
  }else{var inspect=await commands.Run("/usr/bin/python3",new[]{Path.Combine(AppContext.BaseDirectory,"inspect_image.py"),file});if(inspect.Exit!=0)throw new InvalidOperationException("Image is invalid or exceeds 80 megapixels.");}
  var result=await commands.Run("/usr/bin/lp",PrintArguments(r with{Printer=selected.Name,Color=r.Color&&selected.SupportsColor},file),45);
  if(result.Exit!=0)throw new InvalidOperationException("CUPS could not confirm submission. Check the printer queue before retrying. "+result.Error.Trim()[..Math.Min(200,result.Error.Trim().Length)]);
 }
}
