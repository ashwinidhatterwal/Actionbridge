#!/usr/bin/env python3
"""Package a published self-contained linux-x64 host and Linux remote helper."""
import pathlib,shutil,subprocess,sys,hashlib,os
source=pathlib.Path(__file__).resolve().parents[2];runtime=pathlib.Path(sys.argv[1]).resolve();output=pathlib.Path(sys.argv[2]).resolve();output.mkdir(parents=True,exist_ok=True);stage=output/'deb-stage';shutil.rmtree(stage,ignore_errors=True);dest=stage/'usr/lib/actionbridge';dest.mkdir(parents=True)
for p in runtime.iterdir():
 if p.is_file() and p.suffix!='.pdb':shutil.copy2(p,dest/p.name)
for p in (source/'ubuntu/desktop').glob('*.py'):shutil.copy2(p,dest/p.name)
shutil.copy2(source/'publishing/ActionBridge-icon-512.png',dest/'actionbridge.png')
shutil.copy2(source/'ubuntu/PRIVACY.txt',dest/'PRIVACY.txt')
shutil.copy2(source/'ubuntu/THIRD-PARTY-NOTICES.txt',dest/'THIRD-PARTY-NOTICES.txt')
def write(rel,text,mode=0o644):
 p=stage/rel;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(text);p.chmod(mode)
write('usr/bin/actionbridge','#!/bin/sh\nexec /usr/bin/python3 /usr/lib/actionbridge/app.py "$@"\n',0o755)
write('usr/share/applications/app.actionbridge.Ubuntu.desktop','[Desktop Entry]\nVersion=1.0\nType=Application\nName=ActionBridge\nComment=Send files, links and text between your phones and computers\nExec=actionbridge\nIcon=app.actionbridge.Ubuntu\nTerminal=false\nCategories=Utility;Network;\nStartupNotify=true\n')
icon=stage/'usr/share/icons/hicolor/512x512/apps/app.actionbridge.Ubuntu.png';icon.parent.mkdir(parents=True);shutil.copy2(dest/'actionbridge.png',icon)
write('etc/ufw/applications.d/actionbridge','[ActionBridge]\ntitle=ActionBridge device companion\ndescription=Local discovery, secure file transfer and direct WebRTC\nports=45833/tcp|45832,45840:45860/udp\n')
write('DEBIAN/control','''Package: actionbridge
Version: 0.6.0-1
Section: net
Priority: optional
Architecture: amd64
Maintainer: ActionBridge <support@suhagbhandar.in>
Depends: python3 (>= 3.10), python3-gi, gir1.2-gtk-3.0, python3-qrcode, python3-pil, cups-client, poppler-utils, xdg-user-dirs, ca-certificates, libgssapi-krb5-2, libc6 (>= 2.35), libgcc-s1, libstdc++6, zlib1g, libssl3t64 | libssl3
Recommends: cups, cups-filters
Description: Secure device companion for the Ubuntu desktop
 Receive files and desktop actions from Android and send files, text and links
 to your phones and computers. Includes its own runtime and direct WebRTC helper.
''')
write('DEBIAN/postinst','''#!/bin/sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database -q /usr/share/applications || true; fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true; fi
exit 0
''',0o755)
write('usr/share/doc/actionbridge/README.md',(source/'ubuntu/README.md').read_text().replace('(../docs/UPDATE-v0.6.0.md)','(UPDATE-v0.6.0.md)'));write('usr/share/doc/actionbridge/copyright',(source/'ubuntu/THIRD-PARTY-NOTICES.txt').read_text())
write('usr/share/doc/actionbridge/UPDATE-v0.6.0.md',(source/'docs/UPDATE-v0.6.0.md').read_text())
write('usr/share/doc/actionbridge/VERIFICATION.md',(source/'ubuntu/VERIFICATION.md').read_text())
# Normalize reproducible ownership/permissions. Do not ship build intermediates.
for p in stage.rglob('*'):
 if p.is_dir():p.chmod(0o755)
 elif p.name not in ('actionbridge','ActionBridge.Host','ActionBridge.Remote','postinst'):p.chmod(0o644)
for name in ('ActionBridge.Host','ActionBridge.Remote'):
 assert (dest/name).exists(),name+' missing';(dest/name).chmod(0o755)
files=[p for p in stage.rglob('*') if p.is_file() and 'DEBIAN' not in p.relative_to(stage).parts]
write('DEBIAN/md5sums',''.join(hashlib.md5(p.read_bytes()).hexdigest()+'  '+str(p.relative_to(stage))+'\n' for p in sorted(files)))
artifact=output/'ActionBridge-Ubuntu-v0.6.0-amd64.deb';subprocess.run(['dpkg-deb','--root-owner-group','-Zxz','--build',str(stage),str(artifact)],check=True);print(artifact)
