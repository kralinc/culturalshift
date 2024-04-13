using System;
using System.Collections;
using System.Collections.Generic;
using NeoModLoader.api;
using UnityEngine;
using ReflectionUtility;

namespace CulturalShift
{
    public class ModClass : BasicMod<ModClass>
    {
        protected override void OnModLoad()
        {
            // Load your mod here
            // 加载你的mod内容
            Patches.init(this.GetConfig());
            LogInfo("Cultural Shift Mod Loaded");
        }
    }
}