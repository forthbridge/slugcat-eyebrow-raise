﻿using BepInEx;
using System.Security.Permissions;
using BepInEx.Logging;

#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace SlugcatEyebrowRaise
{
    [BepInPlugin("forthbridge.slugcateyebrowraise", "SlugcatEyebrowRaise", "2.0.0")]
    internal class SlugcatEyebrowRaise : BaseUnityPlugin
    {
        public static new ManualLogSource Logger { get; private set; } = null!;

        public const string VERSION = "2.0.0";
        public const string MOD_ID = "slugcateyebrowraise";
        public const string AUTHOR = "forthbridge";

        public void OnEnable()
        {
            Logger = base.Logger;
            Hooks.ApplyHooks();
        }
    }
}