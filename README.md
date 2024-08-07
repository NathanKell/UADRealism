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

### Submarine rnage fix
Submarines can now travel across the map border normally rather than their operational range calculations breaking at the map border in the middle of the Pacific.

### Typo fixes for RandParts
Stock has some typos in its randParts and randPartsRefit assets, and moddders can sometimes have typos too. This detects some of those cases and fixes them.


## Modder Features
### Override assets with CSV files
TAF will look in the Mods folder (where its dll lives) for any csv files named the same as game data TextAssets (for example parts.csv or shipTypes.csv). If such a file exists, it will be loaded rather than the data in resource.assets. This allows modders to distribute just text files rather than an entire resource.assets file.

### Mark 6+ guns
Both the guns and partModels TextAssets can have a `param` column added. For a given gun, any of the mark-based values can be added to the param value with a list of mark followed by value. For example the 5in gun could have a param of `firerate(6;30;7;40), shellV(6;800;7;810)`. This would make it so that the Mark 6 5in gun would have a base fire rate of 30 and shell velocity of 800, and the mark 7 would have 40 and 810 respectively. The mark 5 values are extended for all other mark-based stats if no new value is specified (so mark 5 barrel weight, accuracy, etc would be used for marks 6 and 7. This also holds true for partModels, for example param for a gun could have `model(6;hood_gun_140_x2)` and all other mark 5 parameters would be extended to mark 6 (and 7, if there is a mark 7 of that gun). These new marks can be enabled in the technologies TextAsset just like any other mark.

### Per-shiptype component selection weights
The `param` value of a component now supports `weight_per`. This takes a set of ship types and weights. For example, `weight_per(bb;20;dd;0)` means that for BB ship types, the component's selection weight will be 20, for DD types it will be 0, and for all other types it will be the regular selection weight.

### Tunable conquest events
The hardcoded values in conquest events are tunable now. `taf_conquest_event_chance_mult_starting_duration` (default 0.01) and `taf_conquest_event_chance_mult_full_duration` (default 0.5) control the displayed chance and are the values used at the start and end of the duration. When it's not a land rebellion, `taf_conquest_event_add_chance` (default 0.66) is added to the chance. (This also fixes a bug where the wrong event type checking is used.) Finally `taf_conquest_event_kill_factor` (default 1.0) is the extent to which soldier kill ratio influences conquest chance.

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

### Replacement scrapping behavior
The AI fleet scrapping behavior is optionally completely replaced. Now the AI will scrap ships in order of mothballed ships first (to that target), then active ships (to that target), and finally all ships to the all-ships target tonnage. The target tonnage is determined with a minimum base tonnage and then a coefficient times (shipyard capacity raised to a specified power). Enable by setting `taf_scrap_enable` to 1 in params (you will likely also want to set `min_fleet_tonnage_for_scrap` to 1 or something so that doesn't block scrapping), then tune using the following values:
```
taf_scrap_useOnlyShipAge - if set to 1, ships are scrapped purely in order of age (oldest first). If not present or set to 0, then ships will be scrapped proportionally, so if the fleet has a high tonnage of cruisers, more cruisers will be scrapped than other ships.
taf_scrap_hysteresisMult - In order to not scrap every turn, the AI will only scrap when its fleet tonnage is greater than this multiplier times its scrap target. Once scrapping, it will scrap down to the scrap target, however.
taf_scrap_capacityExponent - an exponent to shipyard capacity. This is multiplied by the appropriate Coeff to get the scrap target tonnage.
taf_scrap_scrapTargetBaseMothball and taf_scrap_capacityCoeffMothball - target tonnage is the base plus the coeff times (shipyard capacity to the capacity Exponent power).
taf_scrap_scrapTargetBaseMain and taf_scrap_capacityCoeffMain - as above, but for active ships.
taf_scrap_scrapTargetBaseTotal and taf_scrap_capacityCoeffTotal - as above but for total fleet tonnage
taf_scrap_multToScrapRequiredVsShipTng - when determining whether to scrap a ship, if a ship's tonnage is greater than this value times the remaining tonnage to scrap to reach the target (either the global target, or the per-shiptype target), then don't scrap this ship.
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

### Serialization support
TAF includes a serialization library for reading and writing CSV files to managed data, as well as the ability to read to BaseData formats on demand from arbitrary files/strings. In addition, a number of the HumanXtoY methods have been reimplemented in managed code, and support exists to read a set of params and update indexed dictionaries (as used by guns, torpedos, and partModels).

# UADRealism
Realism modding for Ultimate Admiral: Dreadnoughts
