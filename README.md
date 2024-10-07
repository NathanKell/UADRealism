# Tweaks And Fixes
A collection of tweaks, fixes, and moddability support features for Ultimate Admiral: Dreadnoughts.

## Installation
* [Download MelonLoader and install it](https://melonwiki.xyz/#/README), either [manually](https://github.com/LavaGang/MelonLoader/releases/latest) or via its installer.
* Download [TweaksAndFixes.dll](https://github.com/NathanKell/UADRealism/raw/main/TweaksAndFixes/bin/Release/net6.0/TweaksAndFixes.dll) and place it in the Mods folder in your UAD folder. (Create the Mods folder if it does not exist. Note the capital M.)
* Run the game. The first launch will be slower but subsequent launches will be normal.

Note that due to the "support old mark guns/torpedos" feature, shared design and campaign saves will become incompatible with stock UAD when they are next saved, and will need hand editing to make them compatible again.


## Player Features and Fixes
### Better shared design support
Instead of matching purely based on year, shared designs now have year windows where they are selectable by the AI. All ships meeting that window are added, even if they were built with technology unavailable to the AI at that time, so long as that technology does not affect the ship. For example, a ship without torpedoes can be selected even if the AI lacks certain torpedo techs the design was created with. Within this set of possible ships, they are ranked based on tech commonality with the AI's current tech base, so newer ships are more likely to be selected. Various parameters can be added to params to control the behavior of this feature:
* `taf_shareddesign_maxYearsIntoFuture` - the maximum number of years ahead of the current year where a design will be considered. Default 10.
* `taf_shareddesign_yearsInPastForSplit` - ships beyond this number of years in the past will be placed in a lower priority bucket to be used only if there are no newer ships, or some of these ships are very close to the oldest new ship. Default 5.
* `taf_shareddesign_yearClosenessAroundSplit` - if an older ship's creation date is closer than this to the oldest new ship, it is included as well. Default 3.
* `taf_shareddesign_bestTechValue` - if at least one ship has greater than this tech commonality, that set of ships will be randomly selected from as the shared design. Default 0.9
* `taf_shareddesign_okTechValue` - if there are no `best` ships, any ships with greater tech commonality than this will be the next selection group. Default 0.75.
* `taf_shareddesign_minTechValue` - if no better ships are available, any ships with greater tech commonality than this will be the final selection group. If no ships make even this level of commonality, no shared design is returned. Default 0.5.
Lastly, when 1890 is the selected year in the shared design constructor, only actual starting techs will be enabled for the ship, rather than any tech with 1890 as its year. This prevents 1890 shared designs being actually unavailable to the AI on campaign start.

### Keep Gun/Torpedo Marks on refit
When refitting a ship, guns and torpedo launchers will be kept at their original mark until the player actively decides to upgrade them (by clicking the Upgrade Mark button appropriate to the caliber, or by the torpedo size, respectively).

### Barbettes mount freely
Barbettes can be placed freely on the deck (like guns) rather than being snapped to the centerline or to mount points.

### Submarine rnage fix
Submarines can now travel across the map border normally rather than their operational range calculations breaking at the map border in the middle of the Pacific.

### Typo fixes for RandParts
Stock has some typos in its randParts and randPartsRefit assets, and moddders can sometimes have typos too. This detects some of those cases and fixes them.


## Modder Features
### Replace/Extend/Override assets with CSV files
TAF can replace existing data with csv files, override data with csv files, or do both. TAF will look in the Mods folder (where its dll lives) for any csv files named the same as game data TextAssets (for example parts.csv or shipTypes.csv). If such a file exists, it will be loaded rather than the data in resource.assets. This allows modders to distribute just text files rather than an entire resource.assets file. In addition, TAF will look for files named assetname_override.csv (for example parts_override.csv). Data in these files will override and extend the existing data (which was loaded either from resource.assets or from csv). Any lines with the name column matching an original entry will override that entry; other lines will be appended to the end of the asset before loading. Note that the "default" line can also be overridden in this way, since it has a valid name. Note that all filenames are case sensitive!

### Replace Language file lines
TAF will look in the Mods folder for files matching the languages that UAD loads (English.lng plus whatever the current language is). Any lines in these files will override the respective languages ingame. Note that only the changed lines need to be placed in these files, since they override one line at a time (like the xxx_override.csv files as described above).

### Don't force tech for predefined ships
The predefined designs feature in 1.6.0.7+, when used in Fast mode, forces AI tech development to never fall behind the ingame year. Set `taf_no_force_tech_with_predefs` to 1 in params to keep AI tech development unchanged even when playing with Fast designs; only designs which match the AI's current tech level will be selected in that case, by choosing a year set of ships (see below) matching the year of the newest (i.e. greatest-techyear) technology of that nation.

### Override predefined designs
UAD 1.6.0.7 introduced a new feature where the game ships with a large number of predefined ship designs that can be used during campaign start or between turns to speed up ship design (rather than generating ships from scratch). However, there is no built-in way to mod these designs. TAF allows overriding this library of ships. Three new options are added to the File Converter popup in the main menu: one that converts Shared Designs in bindesign format to text (JSON) design format, one that exports all Shared Designs into `predefinedDesigns.bin`, and one that saves predefined designs to Shared Designs (either from `predefinedDesigns.bin` if present, or from vanilla resources).

To actually override these designs, TAF offers two approaches. The simplest is to use a single `predefinedDesigns.bin` file in the Mods folder. If it exists, then TAF will load it rather than the built-in predefined designs. Note that any year that has any ships must have at least one ship for each campaign-available nation, since otherwise the game will break. To prevent this breakage, TAF will fail to load a `predefinedDesigns.bin` that has missing nation(s) in at least one of the years in question. For context, UAD ships with 20 designs per ship type per nation in the camapign start years (up to 40 for destroyers!), and 5-10 designs per ship type per nation in various other years (1893, 1914, 1927, 1946, etc) when major tech changes occur in vanilla. For this reason, saving vanilla predefined designs will take some time since there are almost 20,000 of them.

The more complex version of overriding involves creating a `predefinedDesignsData.csv` file in the Mods folder. It lets modders layer multiple sets of predefined designs together in a priority order. This allows, for example, creating a small set of curated, human-designed ships that are used at high priority by the AI (and do not need to cover all nations, all years, and all shiptypes), and a large set of autogenerated ships as a fallback. The system works by trying the highest-priority set first. If the skip chance is met, or the set does not have a ship that matches the criteria (of the given nation and type, within the year range specified) then the next set is tried, and so forth. Any number of sets can be specified. Here is an example `predefinedDesignsData.csv` file:
```
@name,filename,skipChance,yearRange
Custom,customDesigns.bin,0.25,5
Autogenerated,autoDesigns.bin,0,-1
```
The name is unused and simply for descriptive purposes. The filenmae is the file in Mods. The skipChance is the chance this set of designs will be skipped entirely; a nonzero chance means that players can still have variety in their AI opponents even if high priority sets of designs have very few designs. yearRange is the range that defines which designs are acceptable. If this is set to -1, then TAF will follow vanilla rules, trying the desired year and the closest previous year that has ships. If 0 or greater, then any year set within this many years of the desired years will be checked, in order of closest to the desired year. For non-comprehensive sets (i.e. sets that don't contain every shiptype for every nation in every year set) it is strongly recommended to not use -1.

Note that you can specify a filename of `--` which means to use the built-in predefined designs asset. This lets you layer a small set of custom, high-quality predefined designs on top of the large set of built-in designs. Referencing this file will cause issues if mod(s) are installed that change ship parts and stats such that stock designs are no longer legal or working.

### Batch ship generation
To ease in creating predefined designs, TAF reimplements the UI for the built-in batch ship generator. In the main menu, press G. You can then show the years panel and toggle on the years you wish to generate ships for, set the nation(s) to generate for, set the ship type(s), and set the number of ships per nation and per type. When you press Generate, the specified designs will be generated. At the end, you will need to quit the game and relaunch it to get back to the main menu.

### Mark 6+ guns
Both the guns and partModels TextAssets can have a `param` column added. For a given gun, any of the mark-based values can be added to the param value with a list of mark followed by value. For example the 5in gun could have a param of `firerate(6;30;7;40), shellV(6;800;7;810)`. This would make it so that the Mark 6 5in gun would have a base fire rate of 30 and shell velocity of 800, and the mark 7 would have 40 and 810 respectively. The mark 5 values are extended for all other mark-based stats if no new value is specified (so mark 5 barrel weight, accuracy, etc would be used for marks 6 and 7. This also holds true for partModels, for example param for a gun could have `model(6;hood_gun_140_x2)` and all other mark 5 parameters would be extended to mark 6 (and 7, if there is a mark 7 of that gun). These new marks can be enabled in the technologies TextAsset just like any other mark.

### Per-shiptype component selection weights
The `param` value of a component now supports `weight_per`. This takes a set of ship types and weights. For example, `weight_per(bb;20;dd;0)` means that for BB ship types, the component's selection weight will be 20, for DD types it will be 0, and for all other types it will be the regular selection weight.

### Overriding beam/draught limits during AI ship autodesigning
A ship hull can have ai_beamdraughtlimits() in its param column. ai_beamdraughtlimits takes 4 values: new beamMin, new beamMax, new draughtMin, new draughtMax. If the value is 0 for one of them, the original value is kept. For example ai_beamdraughtlimits(-1;1;-2;2) would limit the AI to offets of +/-1% each in beam and +/-2% in draught. ai_beamdraughtlimits(-1;1;0;2) would leave min draught unchanged. If you desire no offset, use a very small number like 0.001.

### Tunable conquest events
The hardcoded values in conquest events are tunable now. `taf_conquest_event_chance_mult_starting_duration` (default 0.01) and `taf_conquest_event_chance_mult_full_duration` (default 0.5) control the displayed chance and are the values used at the start and end of the duration. When it's not a land rebellion, `taf_conquest_event_add_chance` (default 0.66) is added to the chance. (This also fixes a bug where the wrong event type checking is used.) Finally `taf_conquest_event_kill_factor` (default 1.0) is the extent to which soldier kill ratio influences conquest chance.

### Sprite overriding
If the file sprites.csv exists in the Mods folder, TAF will override the specified sprites. For now, just component type sprites are supported. Example sprites.csv:
```
#resource name to override,filename,image width,image height
# For example to override the engine icon with engine.png, make a
# 64x64 PNG file and place it in Sprites, and add this line to this
# file:
#   Components/engine,engine.png,64,64
# Note that transparency in PNGs is supported, so any transparent
# pixels in the PNG will be transparent ingame.
@name,file,width,height
Components/boilers,boilers.png,64,64
```

### Flag overriding
If the file flags.csv exists in the Mods folder, TAF will override the specified flags with the specified flag files (in Mods\Flags). Example flags.csv:
```
#player name,default civil flag filename,default naval jack filename, per-government flags for civil,for naval
# the government types used by the game are: Monarchy, Democracy, Communists, Fascists
# so for example:
#   britain,britain.png,britain.png,"Communists(britainCommunist.png), Democracy(britainRepublic.png)",
# note that flags can be either PNG (no alpha channel) or JPG, and must be 256x128 pixels
@name,civilFlag,navalFlag,civilGovFlags,navalGovFlags
britain,britain.png,britain.png,,
```
Assuming you put a 256x128 png called `britain.png` in the Flags folder under Mods, then Britain's flag will be replaced in all instances by that new png. If you followed the commented example instead, then Britain's flag would be replaced by brtain.png except for its civil flag when government is communist (which would be britainCommunist.png) and when a democracy (which would be britainRepublic.png). Note that you can do this for any player, not just majors, although only majors have government-specific flags.

### Map overriding
While the ports, provinces, etc. TextAssets aren't actually loaded by the game (oddly canals is), TAF allows overriding some port and province data. Add the param `taf_override_map` to params and set it to 2, and TAF will dump the game's built in data for ports and provinces. Make a backup of these dumps, then edit them as desired. Paste the entirety of ports.csv into the ports TextAsset (and same for provinces). Then set `taf_override_map` to 1 in params. This will override port and province data with the data in those assets. This can be combined with editing players, AI admirals, relations matrix, etc, and adding flags (see above) to make other nations playable.

### Replacement armor generation behavior
TAF supports replacing the game's existing armor generating, both the defaults when switching to a new hull and the armor the auto designer creates for ships. It works by constraining armor based on a set of rules which are per-shiptype and vary by year. If the design year is between two rules, the values are interpolated between the rules that exist. If the year lies outside the rules, the nearest rule is used. For example, consider a ruleset with a battleship rule for year 1900 and a battleship rule for 1920. Any battleship designed before 1900 would use the 1900 rule, any battleship designed after 1920 would use the 1920 rule, and a battleship designed in 1915 would use numbers 3/4 of the way from the 1900 rule to alliance_changesthe 1920 rule. The rules are placed in genarmordata.csv in the Mods folder. The file format is as follows (thicknesses are in inches):
```
shipType,year,beltMin,beltMax,beltExtendedMult,turretSideMult,barbetteMult,deckMin,deckMax,deckExtendedMult,turretTopMult,ctMin,ctMax,superMin,superMax,foreAftVariation,citadelMult
bb,1890,9,14,0.5,1.1,1,2,2.5,0.5,1.2,10,15,2,4,0.1,1
bb,1940,10,15,0.5,1.2,1,5,8,0.5,1.2,10,15,2,4,0,1
```
turretSideMult is the multiplier to belt armor used as the default for turrets. barbetteMult also uses belt as its base value, but turretTopMult uses deck. The belt/deckExtendedMult values are multipliers to belt and deck used by fore/aft belt and deck areas respectively. citadelMult is the portion of maximum possible citadel armor used. foreAftVariation is the maximum multiplier to fore/aft armor by which that armor can vary, so a value of 0.02 means the fore armor can be up to +/-2% as thick as default, and aft armor can be up to -/+2% as thick as default. If `taf_genarmor_use_defaults` is set to 1, then the feature will use fallback defaults even if no `genarmordata.csv` file is present; otherwise the feature is only enabled if the file is present.

### Per-shiptype speed min/max params
TAF supports adding the following to each shipType's param: speedMultByGen_max, speedMultByGen_min, and speedMultByGen_rand. These take a list of ship generation and multiplier (or in the case of rand, maximum randomness). They can also take a single value which will apply to all ships of that type regardless of generation.

For example you could add for ca: `speedMultByGen_max(g1;1.01;g2;1.1;g3;1.02;g4;1), speedMultByGen_min(g1;0.8;g2;0.78;g3;0.82;g4;0.85), speedMultByGen_rand(0.05)` and that would, for gen2 ca hulls, have a min speed of 0.78 +/- 5% times speedLimiter and a max of 1.1 +/- 5% times speedLimiter.

Note that you can also override these on a hull-by-hull basis by putting any or all of those params in the hull's param set. So if a particular ca hull had speedMultByGen_max(1.03) instead, the max speed would be speedLimiter times 1.03 +/-5%.

### Limit caliber counts and size
TAF can limit the number of calibers the autodesigner will use for a given battery (main/secondary/tertiary). Note that due to how various parts have different length multipliers, while the caliber may be kept the same, the caliber length may vary between guns. Also note that the game treats casemated guns and turreted guns differently, so a ship may have for example 6.2in secondary turrets but 6.6in casemates.

In main params:
`taf_shipgen_limit_calibercounts_main` (or sec or ter )- the default limit of calibers of that battery (main/secondary/tertiary) to use if no limit is specified in the shiptype params or hull params.

In shiptype param or hull param, you can add `calCount_main` (or sec or ter). This takes either a single value, which is used always, or a set of year;count pairs, for example `calCount_sec(1890;2;1905;0;1915;1)` or `calCount_main(1)`. Anything in a hull's param will override the shipType value for that battery. Note that the year used is the year the hull unlocks, not the year the ship was designed.

On a per-hull basis, TAF can also limit the maximum caliber the AI autodesigner is allowed to select for each battery. Add `ai_max_caliber_main(X)` (or sec or ter) to the hull's params, where X is the maximum caliber in inches that is allowed. This has no effect on the human player. Limits transfer down, so a main limit of 7in and a sec limit of 10in (or no sec limit at all) will still result in a sec limit of 7in.

### Improved alliance mechanics
Set taf_alliance_changes to 1 in params to enable modified alliance mechanics: When a nation A goes to war against a nation B, all A's allies declare war on B and B's allies immediately, and all B's allies declare war on A and A's allies immediately. If any nation is an ally of both A and B, it breaks its alliance with both and gets neutral relations with both.

### Allow non-home population for crew pools
Set `taf_crew_pool_colony_pop_ratio` to a nonzero number between 0 and 1. That portion of non-home population will be added to home population when calculation base crew pool and per-turn crew pool income for a nation.

### Replacement scrapping behavior
The AI fleet scrapping behavior is optionally completely replaced. Now the AI will scrap ships based on their scrap score, which is their age minus (a coefficient times their build time in months). Mothballed ships have a bonus to their score. The target tonnage is determined with a minimum base tonnage (`min_fleet_tonnage_for_scrap`) which is increased by a coefficient times (shipbuilding capacity raised to a specified power). Enable by setting `taf_scrap_enable` to 1 in params, then tune using the following values:
```
taf_scrap_hysteresis - In order to not scrap every turn, the AI will only scrap when its fleet tonnage is greater than the scrap target _plus_ this. Once scrapping, it will scrap down to the scrap target, however.
taf_scrap_buildTimeCoeff - the coefficient to build time
taf_scrap_mothballScoreAddYears - the score to add when a ship is mothballed
taf_scrap_capacityCoeff - the coefficient to (shipbuilding ^ exponent)
taf_scrap_capacityExponent - an exponent to shipbuilding capacity. This is multiplied by the coefficient to get the scrap target tonnage.
```

### Tunable mine behavior
Mine attacks on task forces have been reimplemented so they can be tuned. The following params are supported:
```
taf_mines_max_tf_per_player,-1,Max number of task forces that can be attacked by mines each turn (the same TF can be attacked any number of times by different fields and it counts only once)
taf_mines_max_tf_attacks_per_player,-1,Max number of times mines can attack task forces in a turn (each attack against a TF is a separate instance),,,,,,,
taf_mines_max_ships_per_player,-1,Max number of ships that can be attacked in a turn across all of the task forces of the player and all minefields,,,,,,,
taf_mines_max_ships_per_tf,-1,Max number of ships that can be attacked in a single task force (across any number of minefield attacks),,,,,,,
taf_mines_fleetfactor_mult_war,2,Multiplier to fleet_factor when at war with the field owner,,,,,,,
taf_mines_fleetfactor_mult_peace,2800,Multiplier to fleet_factor when not at war with the field owner,,,,,,,
taf_mines_tonnagefactor_war,5,Multiplier to task force battletonnage when at war (this is used as a cap to minefield damage),,,,,,,
taf_mines_tonnagefactor_peace,0.04,Multiplier to task force battletonnage when at war (this is used as a cap to minefield damage),,,,,,,
taf_mines_max_randomdamagefactor,300000,This is the from value when remapping damage to number of ships hit. The damage is a random value between fleet_factor to tonnagefactor times field radius divided by detection value,,,,,,,
taf_mines_default_max_ships_per_tf,10,When the random damage value is taf_mines_max_randomdamagefactor it gets remapped to this number of ships. A higher value will map to a higher number of ships; there is no clamping,,,,,,,
taf_mines_antimine_min,0.05,The min value to use for the antimine tech,,,,,,,
taf_mines_antimine_max,10,The max value to use for the antimine tech,,,,,,,
taf_mines_ship_damage_percent_min,1,The minimum percent damage to apply to the ship when it hits a mine,,,,,,,
taf_mines_ship_damage_percent_max,200,The maximum percent damage to apply to the ship when it hits a mine. This is before being multiplied by the dmg_factor_ships and by antimine and by the minefield damage mult. Values above 100 are clamped to 100 which means that the default range of 1 to 200 means half the time the ship is sunk and half the time it takes random damage between 1 and 100 percent,,,,,,,
taf_mines_crew_damage_percent_min,5,The minimum percent damage to the crew of the ship,,,,,,,
taf_mines_crew_damage_percent_max,35,The maximum percent damage to the crew of the ship. This is before being multiplied by the dmg_factor_crew and by antimine and by the minefield damage mult.,,,,,,,
```

### Version text
If `taf_versiontext` is set, the version text in the lower right corner of the screen will be replaced. 0 is the default and means no change. 1 means append the supplied text (in the str column) to the existing version string. 2 means replace the text part of the version with the text in the str column but keep the base version number. 3 means entirely replace.

### Serialization support
TAF includes a serialization library for reading and writing CSV files to managed data, as well as the ability to read to BaseData formats on demand from arbitrary files/strings. In addition, a number of the HumanXtoY methods have been reimplemented in managed code, and support exists to read a set of params and update indexed dictionaries (as used by guns, torpedos, and partModels).

# UADRealism
Realism modding for Ultimate Admiral: Dreadnoughts
