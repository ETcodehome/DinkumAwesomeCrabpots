﻿using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using System;
using System.Collections.Generic; // for list

namespace AwesomeCrabpots;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{

    public const string CONFIG_GENERAL              = "General";
    public const int UNITY_LEFT_CLICK               = 0;
    public int portionsRemaining                    = 0;
    public int crabPotTileID                        = 117;
    public List<int> parsedInputs                   = new List<int>();
    public List<int> parsedPortions                 = new List<int>();

    /// <summary>
    /// Config toggle for if the mod gives verbose logging notifications
    /// </summary>
    private readonly ConfigEntry<bool> _verbose;

    /// <summary>
    /// Config item listing what items can be fed into crabpots
    /// </summary>
    private readonly ConfigEntry<string> _validInputs;

    /// <summary>
    /// Controls how many portions you get from each itemID
    /// </summary>
    private readonly ConfigEntry<string> _portionSizes;


    /// <summary>
    /// Plugin constructor - run when the plugin is first generated
    /// </summary>
    public Plugin()
    {
        // assign to config values - has to be in the plugin constructor
        // format is config file section, field name, default value, description

        _verbose = Config.Bind(CONFIG_GENERAL, "Verbose", true, "Enable to see more information about mod behavior in the form of chat notifications and logging events.");

        // standard items were based on wiki page at //2023-08-26
        // cooked meat              = 19        @800        8
        // raw meat                 = 21        @400        4
        // raw turkey drumstick     = 308       @350        4
        // cooked drumstick         = 310       @700        7
        // raw giant drumstick      = 584       @500        5
        // cooked giant drumstick   = 646       @1500       15
        // cooked croco meat        = 647       @1750       18
        // raw croco meat           = 648       @625        6
        // meat pie                 = 685       @2288       23
        // meat on a stick          = 691       @1612       16
        // raw grub meat            = 1168      @ ???       10
        // cooked grub meat         = 1169      @ ???       10
        // apple                    = 300       @107        1
        // banana                   = 297       @248        1
        // bush lime                = 17        @90         1   
        // quandong                 = 770       @110        1
        // animal food              = 344       @50         1
        
        string standardItems = "19,21,308,310,584,646,647,648,685,691,1168,1169,300,297,17,770,344";
        _validInputs = Config.Bind(CONFIG_GENERAL, "validInputs", standardItems, "Comma seperated list of valid item IDs to turn into crabpot bait.");

        string standardPortions = "8,4,4,7,5,15,18,6,23,16,10,10,1,1,1,1,1";
        _portionSizes = Config.Bind(CONFIG_GENERAL, "baitPerItem", standardPortions, "A comma seperated list of bait quantities to gain from each item ID. Matches the position of the valid inputs list.");

        Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} constructor triggered.");
    }


    /// <summary>
    /// Simple in game logging interface that respects verbose flag
    /// </summary>
    /// <param name="output"></param>
    private void Log(string output){
        if (_verbose.Value){
            NotificationManager.manage.createChatNotification(output);            
        }
    }


    /// <summary>
    /// BepInEx plugins dont seem to accept arrays as config value types.
    /// To deal with this, take a comma seperated string and parse it back to an array of ints
    /// </summary>
    public List<int> ParseStringToIntArray(string input){
        string[] words = input.Split(',');
        List<int> result = new List<int>();
        foreach(string id in words){
            try{
                result.Add(Int32.Parse(id));
            }
            catch (FormatException)
            {
                Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} - " + input + " is malformed.");
                // do nothing deliberately - don't let the exception propagate higher
            }
        }
        return result;
    }


    /// <summary>
    /// Triggered when the plugin is activated (not constructed)
    /// </summary>
    private void Awake()
    {
        parsedInputs = ParseStringToIntArray(_validInputs.Value);
        parsedPortions = ParseStringToIntArray(_portionSizes.Value);
    }


    /// <summary>
    /// Will try and fille the crab pot at the players highlighter location
    /// Generates bait from the support item list which allows a single item to fill multiple pots.
    /// If a player already has bait, it will consume that ahead of using the held item.
    /// Bait does not persist between gameplay sessions because it is not saved.
    /// </summary>
    private void TryToFillCrabPot(){
        
        try{

            // Guard clause - Ensure player isn't in a menu
            if (Inventory.inv.isMenuOpen()){
                return;
            }

            // Get the target tile location
            int x = Mathf.RoundToInt(NetworkMapSharer.share.localChar.myInteract.tileHighlighter.transform.position.x / 2f);
            int y = Mathf.RoundToInt(NetworkMapSharer.share.localChar.myInteract.tileHighlighter.transform.position.y / 2f);
            int z = Mathf.RoundToInt(NetworkMapSharer.share.localChar.myInteract.tileHighlighter.transform.position.z / 2f);

            // Get the tile information if available
            int highlighterTileObjectID = WorldManager.manageWorld.onTileMap[x,z];
            if (highlighterTileObjectID == crabPotTileID){

                // Guard clause - Ensure you can't fill already full pots
                TileObject tileObj = WorldManager.manageWorld.findTileObjectInUse(x,z);
                if (tileObj.tileObjectGrowthStages.getShowingStage() > 0){
                    // Suppressed as a bit spammy
                    //Log("This crabpot is already full!");
                    return;
                }

                int portionsToGain = 0;

                // We only do item checking and removal if the player doesn't have enough portions remaining
                if (portionsRemaining == 0){

                    // get currently held item information
                    int invIndex = Inventory.inv.selectedSlot;
                    int heldItemID = Inventory.inv.invSlots[invIndex].itemNo;

                    // Guard clause - Don't proceed if player has empty hands
                    if (heldItemID <= 0){
                        Log("Empty hands won't fill crabpots!");
                        return;
                    }

                    // Guard clause - Only valid crabpot items can be fed into the pots
                    bool canBeFedIntoCrabpot = false;
                    int inputIndex = 0;
                    string heldItemName = Inventory.inv.allItems[heldItemID].getInvItemName();
                    foreach(int id in parsedInputs){
                        inputIndex += 1;
                        if (heldItemID == id){
                            canBeFedIntoCrabpot = true;
                            portionsToGain = parsedPortions[inputIndex - 1];
                            if (portionsToGain != 1) {
                                Log(heldItemName.ToString() + " makes " + portionsToGain + " bait!");
                            }
                            break;
                        }
                    }

                    // Guard clause - player is not holding a valid item type to be added to the crabpot
                    if (canBeFedIntoCrabpot is false){
                        Log(heldItemName.ToString() + " isn't good bait");
                        return;
                    }

                    // subtract the cost item
                    Inventory.inv.invSlots[invIndex].stack -= 1;
                    Inventory.inv.invSlots[invIndex].refreshSlot();
                    
                    // Update player animation if stack is empty so they don't keep holding the consumed item
                    if (Inventory.inv.invSlots[invIndex].stack <= 0){
                        Inventory.inv.consumeItemInHand();
                    }

                    // Add the consumed item to the portion count
                    portionsRemaining = portionsToGain;
                    
                }
                
                // Actually fill the crabpot
                portionsRemaining -= 1;
                int growthStageFilled = 1;
                WorldManager.manageWorld.onTileStatusMap[x, z] = growthStageFilled;
                NetworkMapSharer.share.RpcGiveOnTileStatus(growthStageFilled, x, z);

                // Provide some feedback so it's clear how much bait remains
                if ((portionsRemaining == 0) && (portionsToGain != 1)){
                    Log("You use the last of your bait");
                } else if (portionsToGain == 1){
                    // no logging, just consume the single bait item to prevent being spammed
                } else {
                    Log(portionsRemaining.ToString() + " bait left");
                }

            }

        } catch (NullReferenceException)
        {
            // do nothing deliberately - don't let the exception propagate higher
        }
        
    }


    /// <summary>
    /// Update method called regularly during normal gameplay
    /// </summary>
    private void Update()
    {

        // Check to see if any crabpots need filling using the new logic
        if (Input.GetMouseButtonDown(UNITY_LEFT_CLICK)){
            TryToFillCrabPot();
        }

    }

}
