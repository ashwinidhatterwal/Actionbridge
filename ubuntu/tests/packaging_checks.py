"""Verify a .deb without installing it; extract its host for integration checks."""
import pathlib,sys,subprocess,hashlib,os,py_compile
package=pathlib.Path(sys.argv[1]).resolve();root=pathlib.Path(sys.argv[2]).resolve();root.mkdir(parents=True,exist_ok=True);count=0
def check(v,message):
 global count
 assert v,message
 count+=1;print('PASS:',message)
meta=subprocess.check_output(['dpkg-deb','-f',str(package)]).decode()
check('Package: actionbridge' in meta and 'Version: 0.7.0-1' in meta,'package identity and version')
check('Architecture: amd64' in meta,'amd64 architecture')
check(all(p in meta for p in ['python3-gi','gir1.2-gtk-3.0','cups-client','poppler-utils']),'desktop and printing dependencies declared')
listing=subprocess.check_output(['dpkg-deb','-c',str(package)]).decode().splitlines()
check(all('root/root' in line for line in listing),'every packaged entry has root ownership')
subprocess.run(['dpkg-deb','-x',str(package),str(root)],check=True);subprocess.run(['dpkg-deb','-e',str(package),str(root/'DEBIAN')],check=True)
base=root/'usr/lib/actionbridge'
check(all((base/p).is_file() for p in ['ActionBridge.Host','ActionBridge.Remote','app.py','bridge.py','actionbridge.png']),'host, remote helper, desktop, IPC and icon are included')
check(all(os.access(p,os.X_OK) for p in [base/'ActionBridge.Host',base/'ActionBridge.Remote',root/'usr/bin/actionbridge',root/'DEBIAN/postinst']),'program and installer scripts are executable')
for line in (root/'DEBIAN/md5sums').read_text().splitlines():
 expected,name=line.split('  ',1);assert hashlib.md5((root/name).read_bytes()).hexdigest()==expected,name
check(True,'every packaged payload checksum matches')
check('/usr/bin/python3 /usr/lib/actionbridge/app.py' in (root/'usr/bin/actionbridge').read_text() and 'Exec=actionbridge' in (root/'usr/share/applications/app.actionbridge.Ubuntu.desktop').read_text(),'launcher and desktop entry route to embedded app')
check((root/'usr/share/doc/actionbridge/UPDATE-v0.7.0.md').is_file() and '10.0.12' in (base/'ActionBridge.Host.runtimeconfig.json').read_text(),'update guide and bundled runtime version present')
for name in ['app.py','bridge.py']:py_compile.compile(str(base/name),doraise=True)
print(count,'package checks passed.')
