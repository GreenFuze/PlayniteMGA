# Privacy

This plugin connects to the MyGamesAnywhere server address you configure. It has no analytics or advertising service.

Your profile password is sent to that server once to obtain a scoped access key. The plugin does not persist the password. The access key is encrypted with Windows DPAPI for the current Windows account and stored separately from Playnite settings. Revoke it in the MGA console to disconnect this installation.

Library records, artwork, and game downloads are requested from MGA. Artwork redirects to external providers are not followed. Choosing a storefront play action opens the corresponding installed store or store page, which has its own privacy policy.

Use HTTPS when your server is reached over an untrusted network. Plain HTTP does not encrypt credentials or downloaded data in transit.

The plugin records installation paths and the files it downloaded locally so uninstall can limit removal to recorded files. It leaves additional files and files whose lengths have changed in place.
