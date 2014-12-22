using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace DYJlibrary
{
    class FlapToggler : PartModule
    {
        [KSPField]
        public string flapTransform = "obj_ctrlSrf";
        [KSPField(guiActiveEditor = true, guiName = "Toggle Flaps", isPersistant = true), UI_Toggle()]
        public bool FlapActive = true;
        private bool alreadyActive = true;
        public Transform Flap;

        public override void OnStart(PartModule.StartState state)
        {
            Flap = part.FindModelTransform(flapTransform);
        }

        public void Update()
        {

            print("Active " + FlapActive);
            if (FlapActive == true && alreadyActive == false)
            {
                Flap.gameObject.renderer.enabled = true;
                alreadyActive = true;
            }

            if (FlapActive == false && alreadyActive == true)
            {
                Flap.gameObject.renderer.enabled = false;
                alreadyActive = false;
            }
        }
    }
}    