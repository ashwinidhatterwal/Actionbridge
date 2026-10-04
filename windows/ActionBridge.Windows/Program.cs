namespace ActionBridge.Windows;
static class Program {
    [STAThread] static void Main() {
        using var single=new Mutex(true,"Local\\ActionBridgeCompanion",out var first);
        if(!first) {MessageBox.Show("ActionBridge is already running. Open it from the tray.");return;}
        ApplicationConfiguration.Initialize();
        Application.ThreadException+=(s,e)=>MessageBox.Show(e.Exception.Message,"ActionBridge");
        try {Application.Run(new BridgeForm());}
        catch(Exception ex) {MessageBox.Show("ActionBridge could not start: "+ex.Message,"ActionBridge");}
    }
}
