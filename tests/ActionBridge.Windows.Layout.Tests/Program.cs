using ActionBridge.Windows;
using System.Drawing;
using System.Windows.Forms;
using System.Reflection;
class Program {
 [STAThread] static int Main(){ApplicationConfiguration.Initialize();Directory.CreateDirectory("output/ui-checks");int checks=0;
  foreach(var scale in new[]{1f,1.25f,1.5f,2f})foreach(var size in new[]{new Size(850,620),new Size(1120,780)}){
   using var form=new BridgeForm(false);form.Show();form.AutoScaleMode=AutoScaleMode.None;
   var fonts=Descendants(form).Where(c=>c is Label).Select(c=>(Control:c,Font:c.Font)).ToArray();
   form.Scale(new SizeF(scale,scale));form.Size=new Size((int)(size.Width*scale),(int)(size.Height*scale));
   foreach(var entry in fonts)entry.Control.Font=new Font(entry.Font.FontFamily,entry.Font.Size*scale,entry.Font.Style);
   for(int i=0;i<3;i++){form.PerformLayout();foreach(var control in Descendants(form))control.PerformLayout();Application.DoEvents();}
   foreach(var label in Descendants(form).OfType<Label>().Where(c=>c.Visible&&c.AutoSize)){
    var preferred=label.GetPreferredSize(new Size(label.Width,0));
    if(label.Height+2<preferred.Height)throw new Exception($"Clipped label at {scale}: {label.Text}");
    if(label.Parent is TableLayoutPanel&&label.Bottom>label.Parent.ClientSize.Height-label.Parent.Padding.Bottom+2)throw new Exception($"Clipped table content at {scale}: {label.Text}");checks++;
   }
   using var image=new Bitmap(form.ClientSize.Width,form.ClientSize.Height);form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save($"output/ui-checks/windows-{size.Width}-{scale:0.##}.png");
  }
  using(var owner=new BridgeForm(false)){
   owner.Show();Exception? failure=null;
   using var timer=new System.Windows.Forms.Timer{Interval=300};
   timer.Tick+=(_,_)=>{timer.Stop();var dialog=Application.OpenForms.Cast<Form>().First(f=>f!=owner);
    try{foreach(var label in Descendants(dialog).OfType<Label>().Where(c=>c.Visible&&c.AutoSize)){if(label.Height+2<label.GetPreferredSize(new Size(label.Width,0)).Height)throw new Exception("Clipped pairing instruction: "+label.Text);checks++;}
     using var image=new Bitmap(dialog.ClientSize.Width,dialog.ClientSize.Height);dialog.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save("output/ui-checks/windows-add-device.png");
    }catch(Exception e){failure=e;}finally{dialog.Close();}};
   timer.Start();typeof(BridgeForm).GetMethod("ShowConnectionDialog",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(owner,[2,true]);if(failure!=null)throw failure;
  }
  Console.WriteLine($"Passed {checks} Windows label layout checks at 100–200% scaling.");return 0;
 }
 static IEnumerable<Control> Descendants(Control root){foreach(Control c in root.Controls){yield return c;foreach(var child in Descendants(c))yield return child;}}
}
