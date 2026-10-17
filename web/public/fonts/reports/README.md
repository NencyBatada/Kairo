# Report fonts

Bundled from the Google Fonts repository at commit `47d54ca1ebb14fd864695f1d39dc734f39017c33`. Each family is distributed under the accompanying SIL Open Font License.

- Geist: `ofl/geist/Geist[wght].ttf`, instantiated at weights 400 and 600.
- Instrument Serif: `ofl/instrumentserif/InstrumentSerif-Regular.ttf`, unchanged.
- Noto Sans Devanagari: `ofl/notosansdevanagari/NotoSansDevanagari[wdth,wght].ttf`, instantiated at weights 400 and 600, with width at its default.

Static variants were generated using fontTools 4.65.0 `instantiateVariableFont`, with all axes pinned and font timestamps preserved. React-pdf requires static TTF or WOFF files. The server loads these local assets; PDF creation does not request fonts over the network. Fonts are included under `public/` so the existing standalone container packaging includes them.
