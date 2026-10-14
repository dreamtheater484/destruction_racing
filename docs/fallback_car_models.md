# Fallback Car Models: Pre-Scouted Shortlist (M0)

This is the fallback from plan §8.6, step 6. If the procedural cars fail the M2 art gate, we switch to purchased models that conform to the rig spec. **This is a shortlist only. Nothing has been bought, no accounts were created and no files were downloaded.**

- Scouted on **2026-10-14** by reading the store pages, the public store APIs (Fab, Sketchfab, BlenderKit) and the preview images.
- **Prices** are list prices in USD for the cheapest tier, excluding VAT, unless stated otherwise. Two sales were running on the scouting date: CGTrader (up to 70% off, ending about 15 Oct) and Fab's October Flash Sale (until 17 Oct). Sale prices are noted where seen. Unity Asset Store showed prices in **EUR** for our region; the USD equivalents are estimated ✱.
- **Ratings (1–5)** are my judgement from the preview images, scored against the Wreckfest benchmark (see `reference_board.md` §2). 5 means Wreckfest-level as delivered. "n/r" means the images were not inspected.
- ✱ = not verified (the page did not state it, or I could not open the page).

---

## 0. Key findings

1. **The Sketchfab Store no longer exists.** It closed in 2024 when Epic launched Fab, and paid listings moved to Fab ([Sketchfab blog](https://sketchfab.com/blogs/community/sketchfab-update-what-you-need-to-know-now-that-fabs-live/)). Epic sold Sketchfab and ArtStation to KitBash in August 2026 but kept Fab ([Game Developer](https://www.gamedeveloper.com/business/epics-sells-artstation-and-sketchfab-to-kitbash)). Sketchfab still hosts free CC models and the 3D previews for many Fab listings.
2. **Nothing reaches Wreckfest level out of the box.** The best game-ready, era-correct candidates rate about **3 to 3.5 out of 5**: clean, 40–100k triangles, interiors, but simple materials and 1–2K textures. Wreckfest-level shape accuracy exists only in **render models of real cars** (300–800k polygons, often without game UVs), and those also carry brand problems (point 4).
3. **The best rig-spec fit is TheKnightsKing's "Fully Featured" 1980s cars** (Unity Asset Store and CGTrader). Doors, boot, bonnet, bumpers, front fenders, wheels and all glass are separate objects. They also have an interior, an engine bay, an undercarriage, 4 LODs and a collision mesh, for about $15 each.
4. **Trademark and trade dress are the real risk, not just the badges.**
   - **TurboSquid** marks most real-brand cars **"Editorial Uses Only"**. These cannot be used in a game, and the licence cannot be upgraded. TurboSquid also warns that removing logos may not be enough to make a product generic, because **trade dress** (the overall design) is protected too ([licence limits](https://www.turbosquid.com/help/en/articles/14829413-what-are-the-limitations-and-upgrade-policies-for-3d-model-licenses-on-turbosquid), [associated brands](https://www.turbosquid.com/help/en/articles/9937426-associated-brands-information)).
   - **GM-licensed** TurboSquid products (Chevrolet, Pontiac, Oldsmobile and so on) are allowed only for the use that is approved at purchase ([GM licence restrictions](https://www.turbosquid.com/help/en/articles/9937490-general-motors-license-restrictions)).
   - **CGTrader's** Royalty Free licence covers the seller's rights only, not the car maker's trademarks.
   - Many "generic" models are in fact **near-replicas** (a Fox Mustang, a Monte Carlo SS, an AE86, a Supra, a W123, an E30). We may use them only as an *inspiration base*, then **morph proportions and replace the fascias, lamps and grilles** with our own kits (plan §8.6). That needs a legal sanity check before release.
5. **Every purchased model still needs our pipeline work:** split any missing parts (bumpers, fenders), re-UV the body for the 2048² livery compositor, add the `CUSTOM0` rest-position attribute and the `Crush_*` blend shapes, rename nodes to the rig spec, and swap materials for `car_paint.gdshader`. Budget about 1–3 days per car ✱ (my estimate).

---

## 1. What we need (from plan §8.6)

- **Separate nodes:** Body, Hood, Trunk, Door_FL/FR(/RL/RR), Bumper_F/R, Fender_*, Mirror_L/R, the glass (windshield, rear, sides), Light_Head and Light_Tail L/R, and Wheel_FL/FR/RL/RR (tyre, rim, hubcap), plus an Interior (roll cage, dash, steering wheel).
- **Target LOD0:** about 50–120k triangles including the interior. LODs are a bonus.
- **Formats:** glTF, FBX or .blend are best. DAE and OBJ work. UE-only `.uasset` needs an export step through Unreal Editor.
- **Textures:** PBR, 2–4K, with a body UV we can relayout for liveries.
- **Licence:** commercial game use, usable in Godot, no editorial-only restriction, no AI-only restrictions that block normal use.

---

## 2. Candidates

### C1. TheKnightsKing: "1980s Classic Car Fully Featured" (AE86 Levin–like hatch)
- **URL:** <https://assetstore.unity.com/packages/3d/vehicles/land/1980s-classic-car-fully-featured-246544>
- **Price:** €13.79 (≈ $15 ✱)
- **Licence:** Standard Unity Asset Store EULA, Single Entity. Commercial game use: yes. Use in other engines: allowed if you follow the EULA ([Unity support](https://support.unity.com/hc/en-us/articles/34387186019988-Can-I-use-assets-from-the-Asset-Store-with-other-engines)). Files are fetched through the Unity Editor.
- **Polycount:** LOD1 (detail) 62,814 tris, then 43,771, 19,744 and 12,200 tris.
- **Separate parts:** doors, boot, bonnet, **bumpers**, **front fenders**, wheels, windshield, rear and side windows, wipers and mirrors. Rigged suspension.
- **Interior:** yes, detailed and animatable (dials, pedals, steering wheel, seats), plus a detailed engine bay and undercarriage.
- **Formats:** DAE model plus a Unity prefab.
- **Textures:** mostly 1024², some 512² and 2048²; albedo, metallic and normal maps. This is the weak point.
- **Quality:** 3/5. Clean, accurate silhouette, but basic materials and low-resolution textures.
- **Notes:** very close to a real Corolla Levin, so it must be morphed. Matches #9 Hikari Kaze GT (add pop-ups from our kit).

### C2. TheKnightsKing: "1980s Classic Car #2 Fully Featured" (Supra A70–like, pop-up lamps)
- **URL:** <https://assetstore.unity.com/packages/3d/vehicles/land/1980s-classic-car-2-fully-featured-260065>
- **Price:** €13.79 (≈ $15 ✱)
- **Licence:** as C1.
- **Polycount:** LOD1 102,995 tris, then 75,930, 36,269 and 14,624 tris.
- **Separate parts and interior:** ✱ assumed to be the same "Fully Featured" spec as C1. Only the LOD figures were read.
- **Formats and textures:** as C1 ✱.
- **Quality:** 3/5.
- **Notes:** a wedge coupe with pop-ups, the nearest game-ready base for **#12 Kitsune Rotary GS** (it needs a lower, shorter, glass-hatch morph). Supra trade dress must be removed.

### C3. TheKnightsKing: "1980s Classic Car #3 Fully Featured" (Golf Mk1/Mk2–like hatch)
- **URL:** <https://www.cgtrader.com/3d-models/car/car/1980s-classic-car-3-fully-featured> (also on [Unity](https://assetstore.unity.com/packages/3d/vehicles/land/1980s-classic-car-3-fully-featured-274545))
- **Price:** $14.99 on CGTrader ($10.49 on sale on 14 Oct); €13.79 on Unity.
- **Licence:** CGTrader Royalty Free (no AI). Game use is allowed if end users cannot extract the model ([CGTrader help](https://help.cgtrader.com/hc/en-us/articles/360015124437) ✱ the article title was not opened).
- **Polycount:** 68,116 polygons / 92,084 vertices (CGTrader figures; LOD0, 4 LODs).
- **Separate parts:** doors, boot, bonnet, bumpers, front fenders, wheels and all glass. The engine is rigged.
- **Interior:** yes, detailed, plus engine bay and undercarriage.
- **Formats:** DAE.
- **Textures:** about 1K, as C1 ✱. UVs overlapping.
- **Quality:** 3/5. One buyer review calls it "high quality car model for games".
- **Notes:** base for **#7 Volant 105 GTi** after reshaping the nose and tail.

### C4. TheKnightsKing: "1980s Muscle Car" #1, #2 and #3 (Fox Mustang–, Monte Carlo SS– and Firebird GTA–like)
- **URLs:** [#1](https://assetstore.unity.com/packages/3d/vehicles/land/1980s-muscle-car-277278), [#2](https://assetstore.unity.com/packages/3d/vehicles/land/1980s-muscle-car-2-397798) and [#3](https://assetstore.unity.com/packages/3d/vehicles/land/1980s-muscle-car-3-401438)
- **Price:** €4.59 each (≈ $5 ✱)
- **Licence:** Standard Unity EULA, Single Entity.
- **Polycount:** #1: 49,356 tris; #2: 38,908 tris (4 LODs down to 8,426); #3: 51,616 tris.
- **Separate parts:** ✱ not stated. These are not the "Fully Featured" line, so expect fewer separate parts than C1–C3.
- **Interior:** visible in the previews ✱.
- **Formats:** FBX plus a Unity prefab.
- **Textures:** 512–1024, albedo, metallic and normal maps.
- **Quality:** 3/5. #2's preview is the most convincing.
- **Notes:** #1 or #3 → **#3 Columbia Interceptor GT**; #2 → **#4 Columbia Statesman** (v1.1). They are close replicas of Ford and GM designs.

### C5. lyoshko: "1980s Cars Pack" (5 fictional-looking 80s cars)
- **URL:** <https://www.fab.com/listings/a9e12e7c-fb9f-4799-b172-9b47c0939ec3>
- **Single cars:** [1980s Sedan](https://www.fab.com/listings/f92dc0d1-21db-462c-93b3-779cd2fb4f07), [1980s Hatchback](https://www.fab.com/listings/1b748b1c-1987-41c0-bdbc-bd8b598636cf) and [1980s Muscle Car](https://www.fab.com/listings/1af0ca3a-df07-4dce-8708-5b3c309774d0). The pack also has an SUV and a pickup.
- **Price:** pack $124.99 Personal or $139.99 Professional. Single cars $34.99 Personal (Professional $39.99–44.99).
- **Licence:** Fab Standard licence. Commercial use: yes. Use in any engine: yes. Personal tier if our revenue is under $100k a year, otherwise Professional; both tiers give the same rights ([Fab docs](https://dev.epicgames.com/documentation/en-us/fab/licenses-and-pricing-in-fab)).
- **Polycount:** from the Sketchfab previews: Sedan 81,407 tris, Hatchback 68,279, Muscle 66,836, SUV 86,469, Pickup 76,546. The listing gives 54–72k vertices.
- **LODs:** LOD0–LOD3.
- **Separate parts:** the doors (and boot or hood ✱) open via keys 1–5. The pack has 51 meshes for 5 cars. Bumpers and fenders: ✱ probably not separate.
- **Interior:** yes, detailed, with driver and passenger figures.
- **Formats:** **Unreal Engine only** (`.uasset`). To use it in Godot we must export FBX or glTF from Unreal Editor ✱ (FBX on request from the seller is not stated).
- **Textures:** interior 4096², the rest 128–2048².
- **Quality:** 3/5. Rated 5.0 from 5 buyer ratings.
- **Notes:** a consistent house style. The sedan reads like an early-80s Japanese box (→ **#11 Ryujin Raiden GTS**), the hatch is euro-generic (→ #7), and the muscle car is close to a Camaro (→ #3). Previews: [Sedan on Sketchfab](https://sketchfab.com/3d-models/31eb25046dd94644bfe02586cdac2c16) and [Hatchback](https://sketchfab.com/3d-models/4f0dbe988c1a46d39ce5fb4e53dc1aa9).

### C6. Azunyan: "Retro" series (Sedan 01/02, Coupe 01/02/03, Hatchback 01, '80s SUV)
- **URLs:**
  - [Retro Sedan 01](https://www.fab.com/listings/78ec1358-5017-495e-952f-100539c734d1) (BMW E32–like)
  - [Retro Sedan 02](https://www.fab.com/listings/e22538f1-faf1-4147-9ad6-6dd8a7d55131) (Mercedes W123–like, with a taxi variant)
  - [Retro Coupe 01](https://www.fab.com/listings/0a6ae480-69b7-4393-a737-16cb4fdcb04d) (Datsun 240Z–like, with a rally variant)
  - [Retro Coupe 03](https://www.fab.com/listings/22a17e80-6c2d-4a5f-8f7c-ed68f1ee3bf1) (Buick Regal/Grand National–like)
- **Price:** $14.99–24.99 Personal; $44.99–54.99 Professional.
- **Licence:** Fab Standard licence (any engine).
- **Polycount:** listed as vertices only: Sedan 01 is 33,000 vertices, Hatchback 26,000, the SUV 40,000. The rest ✱.
- **Separate parts:** doors and trunk open with correct pivots, but **the hood does not open** (so it is not a separate part). Dashboard needles are separate.
- **Interior:** yes.
- **Formats:** FBX plus a Unity package. No LODs. Custom collider.
- **Textures:** 1K–4K. Every material comes in **3 conditions: clean, used and abandoned**, which is useful for pre-worn liveries.
- **Quality:** 3/5.
- **Notes:** Coupe 01 → **#10 Tsubasa Hayate 260** (v1.1); Coupe 03 → #4 (v1.1); Sedan 01 or 02 → #6 or #5 bases. All are close replicas.

### C7. Perfectvertexstudio: "80s American station wagon" (woodgrain full-size wagon)
- **URL:** <https://www.fab.com/listings/acd618a9-a028-4916-ac94-b31bcd20e018>
- **Price:** $79.99 Personal, $149.99 Professional.
- **Licence:** Fab Standard licence (any engine).
- **Polycount:** 74,656 tris for the exterior, plus a 3,691-tri low-poly interior and 15,622 tris of wheels.
- **Separate parts:** the steering wheel is separate. Doors, hood and tailgate ✱ not stated.
- **Interior:** low-poly.
- **Formats:** 3ds Max, Blender, FBX, OBJ, plus converted formats ✱.
- **Textures:** PBR 2048² (base colour, metallic, normal, roughness).
- **Quality:** 3.5/5. Convincing woodgrain sides and chrome.
- **Notes:** the **best fit for #2 Aldridge Homestead**. No badges are visible. The same studio sells an 80s box truck, utility van, school bus and pickups, a consistent US fleet for infield dressing. Published 2026-05-08, no reviews yet.

### C8. PADDINGSONS: "American Car 80s HQ" (box-body full-size sedan, Caprice-like)
- **URL:** <https://assetstore.unity.com/packages/3d/vehicles/land/american-car-80s-hq-150038> (sister listing: [Classic US Police Car HQ](https://assetstore.unity.com/packages/package/150028) ✱ not opened)
- **Price:** €23 (≈ $25 ✱)
- **Licence:** Standard Unity EULA, Single Entity.
- **Polycount:** LOD0 54,500 tris; LOD1 26,000; LOD2 6,300; plus a 25,300-tri shadow caster.
- **Separate parts:** "fully prepared for animation, interior and exterior" ✱ (the exact part list is not given).
- **Interior:** yes.
- **Formats:** Unity package (FBX inside ✱). Source files are available on request.
- **Textures:** 4096² exterior and interior PBR; glass 2048².
- **Quality:** 3.5/5. A buyer review says "Amazing Quality". Released in 2019.
- **Notes:** the **best fit for #1 Aldridge Brougham 400** after a fascia and roofline morph.

### C9. flawlessnormals (az9): "Old Classic car Sedan Set" (W123-like)
- **URL:** <https://www.cgtrader.com/3d-models/car/car/old-classic-car-sedan-set-with-interior>
- **Price:** $59.16 list ($29.58 on sale on 14 Oct).
- **Licence:** CGTrader Royalty Free (no AI).
- **Polycount:** 29,410 tris / 17,470 vertices.
- **Separate parts:** doors, trunk, front seats, steering wheel, wheels, wheel caps, brake discs and a taxi sign. Hood and bumpers ✱ not listed.
- **Interior:** yes.
- **Formats:** Blender (native), FBX, OBJ, DAE.
- **Textures:** PBR, **3 states (clean, dirt, abandoned)**, 9 colour variants.
- **Quality:** 3/5.
- **Notes:** an EU boxy-sedan base for **#5 Nordvik Fjord DL**, but the shape is far from a Volvo "brick" and needs a large morph. The seller also offers a "Set of 5 sedan cars" ($299 list ✱ contents not checked).

### C10. PurePolygon: "Classic American 1980s Coupe" (Monte Carlo–like)
- **URL:** <https://www.cgtrader.com/3d-models/vehicle/other/classic-american-1980s-coupe-vintage-sedan-4-lods-game-ready>
- **Price:** $25 one-off purchase (it is also included in CGTrader's subscription).
- **Licence:** CGTrader Royalty Free (no AI).
- **Polycount:** LOD0 48,450, then LOD1 17,900, LOD2 1,900 and LOD3 950.
- **Separate parts:** ✱ not stated.
- **Interior:** basic low-poly.
- **Formats:** FBX, OBJ, **glTF**.
- **Textures:** 4K PNG PBR, in sets for Unreal (ORM), Unity HDRP and Unity URP.
- **Quality:** 2.5/5.
- **Notes:** an alternative for #4. Published 2026-06-02.

### C11. artbonvic: "Euro Classic Car 1980s Coupe" (BMW E30 two-door–like)
- **URLs:** [Fab](https://www.fab.com/listings/3026f10f-1f45-465d-b822-60d73deafca7) and [TurboSquid](https://www.turbosquid.com/3d-models/euro-classic-car-1980s-coupe-3d-model-2611750)
- **Price:** $9.99 on Fab, $5 on TurboSquid.
- **Licence:** Fab Standard licence (the TurboSquid licence flag ✱ not checked).
- **Polycount:** about 23k tris.
- **Separate parts:** wheels, body, spoiler, mirrors, wipers, door handles and lights. Doors and hood ✱ not mentioned.
- **Interior:** ✱
- **Formats:** Blender, FBX, OBJ, glTF/GLB, Unity URP. The listing mentions Godot.
- **Textures:** untextured car-paint material plus baked light textures.
- **Quality:** 2.5/5.
- **Notes:** the only game-ready candidate for **#6 Adler Falke 2.7i**, and too light for a hero car. Published 2026-08-28.

### C12. Ozgur Saral: "Classic Sedan 1, R 12 (Driveable)" (Renault 12–like) and "Muscle Car 1"
- **URLs:** [Classic Sedan 1](https://www.fab.com/listings/c38b55a2-3fb8-4f66-8257-ec87632acd55) and [Muscle Car 1](https://www.fab.com/listings/9ff0c589-d505-423e-bb96-20d7e02390f8)
- **Price:** $39.99 Personal / $69.99 Professional each.
- **Licence:** Fab Standard licence.
- **Polycount:** Sedan 53k tris / 29k vertices; Muscle Car 60k tris / 45k vertices.
- **Separate parts:** body, doors and wheels are imported separately; skeletal driveable rig. Hood and trunk ✱.
- **Interior:** ✱
- **Formats:** Unreal Engine, Blender, FBX.
- **Textures:** ✱
- **Quality:** 3/5.
- **Notes:** a 70s Euro-saloon flavour. A possible #8 Ashford Kestrel RS base (v1.1) after reshaping.

### C13. Squir: "Volvo 240 sedan 1993" (render-grade, real brand)
- **URL:** <https://www.cgtrader.com/3d-models/car/car/volvo-240-sedan-1993>
- **Price:** $159 list ($79.50 on sale on 14 Oct).
- **Licence:** CGTrader Royalty Free (no AI). This does **not** clear Volvo's trademarks or trade dress.
- **Polycount:** 500,000 polygons. Not unwrapped for games ("Unwrapped UVs: No").
- **Separate parts:** grouped as a render model ✱.
- **Interior:** ✱ (typical for Squir, but not checked for this model).
- **Formats:** 3ds Max (mental ray), OBJ, FBX, 3DS, LWO, C4D.
- **Textures:** render materials.
- **Quality:** 4/5 for shape and accuracy, but not game-ready.
- **Notes:** Squir is the largest consistent catalogue of era cars (7,127 products on TurboSquid), covering nearly every archetype. Example: [Chevrolet Caprice sedan on TurboSquid](https://www.turbosquid.com/3d-models/chevrolet-caprice-sedan-1980-1985-864623) at $199, 769k polygons, an **official GM-licensed product** whose use must be approved. Use only as a reference or retopology source.

### C14. RENDERSPAZ: "1986 Chevy Monte Carlo, Stock Car" (render-grade, real brand)
- **URL:** <https://www.cgtrader.com/3d-models/car/racing-car/1986-chevy-monte-carlo-nascar>
- **Price:** $140 list (shown at −70% in search results on 14 Oct).
- **Licence:** CGTrader Royalty Free. The Chevrolet (GM) brand is depicted.
- **Polycount:** 486,522 polygons.
- **Separate parts:** ✱
- **Interior:** low-poly block-outs only.
- **Formats:** 3ds Max (V-Ray), OBJ, FBX, USD, DAE, 3DS, ABC.
- **Textures:** **non-overlapping UVs plus a PSD livery template**, which is a nice fit for our livery workflow.
- **Quality:** 3.5/5.
- **Notes:** a pure DD2 stock-car vibe. Needs retopology and a brand-free reshape.

### C15. BlackSpring Designs: "Mazda RX-7 1985 (Low Interior)" (excluded)
- **URL:** <https://www.turbosquid.com/3d-models/mazda-rx-7-1985-low-interior-1023098>
- **Price:** $79
- **Licence:** TurboSquid Standard, but flagged **"Editorial Uses Only"**, so it is **not allowed in a game**.
- **Polycount:** 339,000 polygons / 452,000 vertices, quads, non-overlapping UVs.
- **Formats:** 3ds Max 2012 (V-Ray), FBX, OBJ.
- **Quality:** n/r.
- **Notes:** listed only as a typical example of the editorial trap. BlackSpring has 1,028 products, many in our era, and they are all subject to the same check.

### C16. SANTICUSTOMS: "Toyota AE86 SANTI Violet Sakura"
- **URL:** <https://www.cgtrader.com/3d-models/car/sport-car/toyota-ae86-santi-violet-sakura>
- **Price:** $39.90
- **Licence:** CGTrader Royalty Free (no AI). It uses Toyota's name.
- **Polycount:** 89,986 polygons / 71,655 vertices.
- **Separate parts:** doors, hood and trunk open; "separated body parts"; light groups; collision mesh.
- **Interior:** ✱ (dashboard needles are animated).
- **Formats:** FBX, GLB, BLEND.
- **Textures:** **512² only** (13 maps).
- **Quality:** 2/5 for us. A heavy anime-livery widebody kit, the wrong era style.
- **Notes:** published on the scouting date, with no reviews. The same seller has an "E34" at $19.90 (42.7k polygons).

### C17. Kanistra Studio (BlenderKit): "Old City Sedan" (Caprice-like background car)
- **URL:** <https://www.blenderkit.com/asset-gallery-detail/abf7695f-2090-4c9c-89a6-a2b5e6de2032>
- **Price:** requires a paid BlenderKit plan (the asset is not free; plan price ✱).
- **Licence:** BlenderKit Royalty Free ✱ (licence text not re-read).
- **Polycount:** 12,441 faces; exterior only; 12 objects.
- **Formats:** Blender. Procedural Cycles materials, no textures.
- **Quality:** n/r. Background quality only.
- **Notes:** the same studio has Retro Muscle Car, Retro Coupe and Luxury Retro Sedan at 12–26k faces. **Not suitable for hero cars.** At most usable for parked infield cars.

**Searched with little to show:**
- **Gumroad:** no era-car game assets found; the hits were free low-poly cars.
- **Superhive (Blender Market):** Rafael Lugo's cars have separated parts, but are 1930s–50s.
- **Hum3D (now 3dmodels.org):** blocked by a bot check, so not scouted ✱.

---

## 3. Packs and series (one artist, one style)

Consistency matters more than any single model, because mixing sellers mixes detail density (23k–103k tris) and modelling style.

| Artist / series | Store | Era cars available | Style and consistency | Fit for us |
|---|---|---|---|---|
| **TheKnightsKing**: 1950s–1980s series (1980s Classic Car #1–3 Fully Featured, 1980s Muscle Car #1–3, 1970s Muscle Car #1–14, plus bundles such as [60s/70s/80s Car Bundle #6](https://assetstore.unity.com/packages/3d/vehicles/land/60s-70s-and-80s-car-bundle-6-6-cars-403004), €23 for 6 cars) | Unity, some on CGTrader | Mustang-, Monte Carlo-, Firebird-, AE86-, Supra- and Golf-likes, many 70s US muscle cars | **Very consistent** studio renders, the same feature set and texture approach | **Best rig fit and cheapest.** Low texture resolution. Close replicas. |
| **lyoshko**: [1980s Cars Pack](https://www.fab.com/listings/a9e12e7c-fb9f-4799-b172-9b47c0939ec3) (5 cars) | Fab (UE format) | Sedan, hatch, muscle car, SUV, pickup | Consistent, with LODs and interiors; designs read as generic | Good, but needs a UE export step. Only 3 of the 5 cars are useful. |
| **Azunyan**: Retro series (7 cars) | Fab (FBX/Unity) | E32-, W123-, 240Z-, Grand National-, Lada Oka-likes, an 80s SUV | Consistent, with clean/used/abandoned materials | Good for pre-worn looks. No separate hood. Replicas. |
| **Perfectvertexstudio**: 80s US fleet | Fab | Woodgrain wagon, plus box truck, vans, school bus, pickups | Consistent PBR 2K | Wagon (#2) plus infield props. |
| **flawlessnormals (az9)**: sedan and coupe sets | CGTrader | W123-like sedan, a classic coupe, a "set of 5 sedans" | Consistent Marmoset look with 3 wear states | One or two EU bases. |
| **Squir**: render catalogue | CGTrader, TurboSquid | Almost every archetype, real brands | Very consistent render-grade work | Retopology source only, with brand and trade-dress issues. |

---

## 4. Summary table

Price is the Personal tier or list price. Licence "OK" means commercial game use in Godot is allowed under the store licence; brand and trade-dress risk is a separate column.

| # | Candidate | Store | Price | Game licence | LOD0 tris | Parts separate | Interior | Formats | Textures | Brand risk | Rating | Best roster fit |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| C1 | TKK 1980s Classic FF (AE86-like) | Unity | €13.79 | OK (Unity EULA) | 62.8k | **Doors, hood, trunk, bumpers, fenders, wheels, glass** | Yes, detailed | DAE | 0.5–2K | High (replica) | 3 | #9 |
| C2 | TKK 1980s Classic #2 FF (Supra-like) | Unity | €13.79 | OK | 103k | As C1 ✱ | Yes ✱ | DAE ✱ | ✱ | High | 3 | #12 |
| C3 | TKK 1980s Classic #3 FF (Golf-like) | CGTrader / Unity | $14.99 | OK (RF) | 68k | As C1 | Yes | DAE | ~1K | High | 3 | #7 |
| C4 | TKK 1980s Muscle #1–3 | Unity | €4.59 each | OK | 39–52k | ✱ | Yes ✱ | FBX | 0.5–1K | High | 3 | #3, #4 |
| C5 | lyoshko 1980s Cars Pack | Fab | $124.99 (single $34.99) | OK (Fab Std) | 67–86k | Doors (hood/trunk ✱) | Yes, with figures | UE only | 4K interior, ≤2K other | Low–medium | 3 | #11, #7, #3 |
| C6 | Azunyan Retro series | Fab | $14.99–24.99 | OK | ~26–40k verts | Doors and trunk (no hood) | Yes | FBX, Unity | 1–4K, 3 wear states | High | 3 | #10, #4, #5/#6 |
| C7 | Perfectvertexstudio 80s wagon | Fab | $79.99 | OK | 74.7k (+wheels 15.6k) | Steering wheel; doors ✱ | Low-poly | Max, Blend, FBX, OBJ | 2K PBR | Low–medium | 3.5 | **#2** |
| C8 | PADDINGSONS American Car 80s HQ | Unity | €23 | OK | 54.5k | "Animation-ready" ✱ | Yes | Unity (FBX ✱) | 4K PBR | Medium (Caprice-like) | 3.5 | **#1** |
| C9 | flawlessnormals Old Classic Sedan | CGTrader | $59.16 ($29.58 sale) | OK (RF) | 29.4k | Doors, trunk, wheels | Yes | Blend, FBX, OBJ, DAE | 4K PBR, 3 states | High (W123) | 3 | #5 (weak) |
| C10 | PurePolygon 80s Coupe | CGTrader | $25 | OK (RF) | 48.5k | ✱ | Basic | FBX, OBJ, glTF | 4K PBR | High | 2.5 | #4 alt |
| C11 | artbonvic Euro 80s Coupe | Fab / TurboSquid | $9.99 / $5 | OK (Fab) | ~23k | Wheels, trim, lights | ✱ | Blend, FBX, glTF, Unity | Untextured paint | High (E30) | 2.5 | #6 (weak) |
| C12 | Ozgur Saral Classic Sedan 1 / Muscle 1 | Fab | $39.99 | OK | 53k / 60k | Body, doors, wheels | ✱ | UE, Blend, FBX | ✱ | Medium | 3 | #8 |
| C13 | Squir Volvo 240 (render) | CGTrader | $159 | OK (RF), brand not cleared | 500k | Render groups ✱ | ✱ | Max, FBX, OBJ… | Render | **Very high** | 4 (shape) | #5 reference |
| C14 | RENDERSPAZ '86 Monte Carlo stock car | CGTrader | $140 | OK (RF), GM brand | 487k | ✱ | Block-outs | Max, FBX, USD… | UVs + PSD livery template | **Very high** | 3.5 | #4 / DD2 vibe |
| C15 | BlackSpring RX-7 1985 | TurboSquid | $79 | **No (editorial only)** | 339k | — | Upper part | Max, FBX, OBJ | — | — | n/r | excluded |
| C16 | SANTICUSTOMS AE86 | CGTrader | $39.90 | OK (RF), Toyota name | 90k | Doors, hood, trunk, body parts | ✱ | FBX, GLB, Blend | 512² | High | 2 | — |
| C17 | Kanistra Old City Sedan | BlenderKit | Paid plan | ✱ RF | 12.4k | 12 objects | No | Blend | Procedural | Medium | n/r | infield only |

---

## 5. Recommendation

### Top 3
1. **TheKnightsKing "Fully Featured" 1980s cars (C1–C3), plus its muscle cars (C4) for the pony car.** This is the closest match to our rig spec: separate bumpers and fenders, glass, interior and LODs. It is consistent and very cheap. It covers **#3, #7, #9 and #12**. The minuses are 1K textures (we re-texture anyway) and replica shapes (we morph anyway).
2. **lyoshko 1980s Cars Pack (C5).** A consistent house style with designs that read as generic, LOD0–3, good interiors and strong buyer ratings. It covers **#11 (sedan), and alternatives for #7 and #3**. The minus is that it is UE-only, so we need an Unreal Editor export step and a check of which panels are separate.
3. **For the US heavyweights: Perfectvertexstudio 80s wagon (C7) + PADDINGSONS American Car 80s HQ (C8).** These have the highest material quality in the game-ready set (2–4K PBR, 55–75k tris) and are the only good fits for **#2 Homestead and #1 Brougham 400**.

**Gaps:** there is no good game-ready base for **#5 Nordvik Fjord DL** (a Volvo-240-type "brick") or **#6 Adler Falke** (an E30 type). The available options are weak (C9, C11) or are render-grade real-brand models (C13). Since the Nordvik is already in the M2 art spike, keep it procedural, or commission it, even if we fall back for the other cars.

### Estimated cost for the 9 v1 cars (plan A, Personal tier, list prices, excluding VAT)

| Roster car | Base model | Price |
|---|---|---|
| #1 Aldridge Brougham 400 | C8 PADDINGSONS 80s HQ | €23 (≈ $25) |
| #2 Aldridge Homestead | C7 Perfectvertexstudio wagon | $79.99 |
| #3 Columbia Interceptor GT | C4 TKK 1980s Muscle Car #1 or #3 | €4.59 (≈ $5) |
| #5 Nordvik Fjord DL | C9 flawlessnormals sedan (big morph) | $59.16 |
| #6 Adler Falke 2.7i | C11 artbonvic coupe (or Azunyan Sedan 01, $19.99) | $9.99 |
| #7 Volant 105 GTi | C3 TKK Classic #3 FF | $14.99 |
| #9 Hikari Kaze GT | C1 TKK Classic FF | €13.79 (≈ $15) |
| #11 Ryujin Raiden GTS | C5 lyoshko 1980s Sedan | $34.99 |
| #12 Kitsune Rotary GS | C2 TKK Classic #2 FF | €13.79 (≈ $15) |
| **Total** | | **≈ $260** (about $130–180 at the sale prices seen on 14 Oct ✱) |

- **Plan B (consistency first):** the whole lyoshko pack ($124.99) plus TKK C1 and C2, C7, C8, C9 and Azunyan Sedan 01. **≈ $340.**
- **Professional tier:** if our revenue is over $100k a year, the Fab Professional prices apply (the wagon is $149.99, the lyoshko sedan $44.99). Add about $80 to plan A.
- **Render-grade real-brand bases (Squir level):** about $65–199 each, so **≈ $600–1,500 for 9**, plus heavy retopology and UV work and a brand-licensing problem. **Not recommended.**

**Next step, if you approve:** buy *one* cheap candidate (C1, ≈ $15) as an M2 pipeline test: import it, split it to the rig spec, add crush shapes and check it in the photo studio. That shows the real per-car conversion cost before we commit to the rest.

---

## 6. Licence notes and sources

- **Fab Standard licence:** Personal tier (under $100k revenue in the last 12 months) or Professional tier, with the same rights; any engine; no standalone redistribution. Avoid listings still under the legacy **"UE Marketplace License"**, which is Unreal-only (for example ModelQC's "Drivable retro car Caviro"). Source: [Fab licences and pricing](https://dev.epicgames.com/documentation/en-us/fab/licenses-and-pricing-in-fab).
- **Unity Asset Store:** Standard EULA, Single Entity. Use in other engines is allowed if the EULA is followed. Assets are downloaded through the Unity Editor, so extracting the FBX or DAE needs a Unity install. Source: [Unity support](https://support.unity.com/hc/en-us/articles/34387186019988-Can-I-use-assets-from-the-Asset-Store-with-other-engines).
- **CGTrader:** Royalty Free allows embedding in a game if users cannot extract the asset; "(no AI)" restricts use for AI and ML. Editorial-licensed items are not for games. RF does not grant the car maker's trademark rights.
- **TurboSquid:** see [the licence](https://www.turbosquid.com/licensing). Editorial items can't be used in games and can't be upgraded. Generic status needs more than removing logos (trade dress). GM-licensed products need use approval.
- **Real-brand policy for us:** use any real-car model only as a shape base. Change the proportions and swap the fascias, lamps, grilles and bumpers for our own detail kits, with no badges, no model names and no logos on textures. Get a quick legal review before release.
