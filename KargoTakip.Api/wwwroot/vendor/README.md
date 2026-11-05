# Third-party files

Everything here is copied unchanged from the npm packages below, so the app works without a CDN
and the pages never load code or fonts from someone else's server at run time.
(Map *tiles* are the one exception: Leaflet loads them from OpenStreetMap while a map is visible.)

| Folder | Package | Version | License |
|---|---|---|---|
| `fonts/` (Barlow) | `@fontsource/barlow` | 5.3.0 | SIL Open Font License 1.1 (`licenses/barlow-OFL.txt`) |
| `fonts/` (Barlow Condensed) | `@fontsource/barlow-condensed` | 5.3.0 | SIL Open Font License 1.1 |
| `leaflet/` | `leaflet` | 1.9.4 | BSD 2-Clause (`licenses/leaflet-BSD-2-Clause.txt`) |
| `qrcode/` | `qrcode-generator` | 2.0.4 | MIT (license text is in the header of `qrcode.mjs`) |

To update one of them: `npm install <package>@<version>` in a scratch folder and copy the files again.
