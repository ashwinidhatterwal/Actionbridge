using System.Diagnostics;
namespace ActionBridge.Ubuntu;
public record CommandResult(int Exit,string Output,string Error);
public interface ICommands {Task<CommandResult> Run(string executable,IEnumerable<string> arguments,int timeoutSeconds=20);}
public sealed class Commands:ICommands {
 public async Task<CommandResult> Run(string executable,IEnumerable<string> arguments,int timeoutSeconds=20){
  var info=new ProcessStartInfo(executable){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var a in arguments)info.ArgumentList.Add(a);info.Environment["LC_ALL"]="C";
  using var process=new Process{StartInfo=info};try{process.Start();}catch(Exception e){throw new InvalidOperationException(Path.GetFileName(executable)+" is unavailable. Install the required Ubuntu dependencies.",e);}
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));var stdout=ReadBounded(process.StandardOutput,timeout.Token);var stderr=ReadBounded(process.StandardError,timeout.Token);
  try{await process.WaitForExitAsync(timeout.Token);return new(process.ExitCode,await stdout,await stderr);}catch{try{if(!process.HasExited)process.Kill(true);}catch{}throw new InvalidOperationException(Path.GetFileName(executable)+" did not finish. Check the application or printer before retrying.");}
 }
 static async Task<string> ReadBounded(StreamReader reader,CancellationToken ct){var text=new System.Text.StringBuilder();var buffer=new char[4096];while(true){var n=await reader.ReadAsync(buffer,ct);if(n==0)break;if(text.Length+n>1048576)throw new InvalidOperationException("Command response too large.");text.Append(buffer,0,n);}return text.ToString();}
}
