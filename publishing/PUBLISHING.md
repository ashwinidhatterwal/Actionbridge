# Remote pilot publication status

v0.3.0 is a pilot. Do not use the earlier v0.2.0 publication checklist as proof that remote access is production ready. Complete the cloud deployment, enrollment/abuse policy, real device and relay tests, public privacy notice/contact details and updated screenshots first. See remote/DEPLOY.md.

Earlier release preparation notes follow:

# Publication handoff — 0.2.0

This is a signed release candidate, not an already approved store release.

## Install and test
Update Windows by extracting the new ZIP and running Install.cmd. The previous certificate, history and received files stay in place.
The new Android release signature differs from the first debug build. Uninstall that old test APK once, install the release APK, and approve the phone again. This clears old phone-side history; PC files remain.

## Android release
The package is app.actionbridge, version code 20, version 0.2.0, min API 26, target API 36. A stable upload key is supplied separately in the PRIVATE signing kit. Do not upload that kit to a public source repository. Preserve it for future updates.
Use the AAB for Play Console and the APK for direct installation/testing. Configure Play App Signing when creating the release. APKs installed via Play may have a different signing certificate than the sideloaded release, depending on app-signing setup.

Before store submission:
- Supply a public support email. Replace the draft policy's contact section and host it at a publicly accessible URL. Add the URL to Play Console.
- Host the Windows companion and add its actual link to the listing; users need that companion.
- Capture real phone screenshots of the built release. Included artwork is not a screenshot.
- Complete Data Safety by reviewing the actual implementation and Play's definition of collection/sharing. Files, connection IDs and local addresses are transmitted to the user's approved PC; avoid asserting an exemption without checking the current form. No developer server, ads or analytics are included.
- Declare foreground service dataSync for user-initiated transfers and provide Play's requested explanation/demo video.
- Complete content rating, target audience and any testing/access requirements shown by this developer account.
- Test release installation, Share from Files and WhatsApp, initial/repeated connection, large-file resume, notification cancellation, printer offline, real PDF page range, duplex and collated copies. Test at least a second PC/phone combination.

## Build reproducibly
The source includes .github/workflows/release.yml. Add the four upload signing secrets from the private kit; UPLOAD_KEYSTORE_BASE64 contains the JKS encoded as base64. Set SUPPORT_EMAIL as a repository variable. The workflow tests and builds signed APK/AAB plus Windows artifacts. It deletes the temporary key after the build.
The older build.yml produces debug test artifacts only.

## Windows distribution
The companion is self-contained and installed per user using Install.cmd; firewall creation needs Windows administrator approval. The executable has an application icon and version information. It is not Authenticode signed. For broad public distribution, use a publisher-controlled code-signing certificate and timestamp the executable, then validate the signed package. Do not tell users to disable Windows security protections. Microsoft Store/MSIX submission is a separate packaging route, not included here.

## References
https://developer.android.com/studio/publish/app-signing
https://developer.android.com/studio/publish/upload-bundle
https://support.google.com/googleplay/android-developer/answer/13392821
