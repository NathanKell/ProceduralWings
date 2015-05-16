using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Reflection;



public class WingManipulator : PartModule, IPartCostModifier
{
    // PartModule Dimensions
    [KSPField]
    public float modelChordLenght = 2f;

    [KSPField]
    public float modelControlSurfaceFraction = 1f;

    [KSPField]
    public float modelMinimumSpan = 0.05f;

    [KSPField]
    public Vector3 TipSpawnOffset = Vector3.forward;

    // PartModule Part type
    [KSPField]
    public bool symmetricMovement = false;

    [KSPField]
    public bool doNotParticipateInParentSnapping = false;

    [KSPField]
    public bool isWing = true;

    [KSPField]
    public bool isCtrlSrf = false;

    [KSPField]
    public bool updateChildren = true;

    [KSPField(isPersistant = true)]
    public bool relativeThicknessScaling = true;

    // PartModule Tuning parameters
    [KSPField]
    public float liftFudgeNumber = 0.0775f;
    
    [KSPField]
    public float massFudgeNumber = 0.015f;

    [KSPField]
    public float dragBaseValue = 0.6f;

    [KSPField]
    public float dragMultiplier = 3.3939f;

    [KSPField]
    public float connectionFactor = 150f;

    [KSPField]
    public float connectionMinimum = 50f;

    [KSPField]
    public float costDensity = 5300f;

    [KSPField]
    public float costDensityControl = 6500f;

    // Commong config
    public bool loadedConfig = false;
    public string keyTranslation = "g";
    public string keyTipScale = "t";
    public string keyRootScale = "r";
    public float moveSpeed = 5.0f;
    public float scaleSpeed = 0.25f;

    // Internals
    public Transform Tip;
    public Transform Root;

    private Mesh baked;

    public SkinnedMeshRenderer wingSMR;
    public Transform wingTransform;
    public Transform SMRcontainer;

    private float updatedRootScaleZ;
    private float updatedTipScaleZ;

    private float cachedtipthicknessMod;
    private float cachedrootThicknessMod;

    private bool FARactive = false;
    private bool NEARactive = false;
    private bool FARmass = false;

    public Vector3 scaleMultipleRoot;
    public Vector3 scaleMultipleTip;

    private bool justDetached = false;

    // Internal Fields
    [KSPField(guiActiveEditor = true, guiName = "RootThickness", isPersistant = true, guiUnits = "%"), UI_FloatRange(minValue = 0.1f, maxValue = 5f, stepIncrement = 0.02f)]
    public float rootThicknessMod = 1f;

    [KSPField(guiActiveEditor = true, guiName = "TipThickness", isPersistant = true, guiUnits = "%"), UI_FloatRange(minValue = 0.1f, maxValue = 5f, stepIncrement = 0.02f)]
    public float tipThicknessMod = 1f;

    [KSPField]
    public Vector3 tipScaleModified;

    [KSPField]
    public Vector3 rootScaleModified;

    [KSPField(isPersistant = true)]
    public Vector3 tipScale;
    
    [KSPField(isPersistant = true)]
    public Vector3 tipPosition = Vector3.zero;

    [KSPField(isPersistant = true)]
    public Vector3 rootPosition = Vector3.zero;
    
    [KSPField(isPersistant = true)]
    public Vector3 rootScale;

    [KSPField(isPersistant = true)]
    public bool IgnoreSnapping = false;

    [KSPField(isPersistant = true)]
    public bool SegmentRoot = true;

    [KSPField(isPersistant = true)]
    public bool IsAttached = false;

    // Intermediate aerodymamic values 
    public double Cd;

    public double Cl;

    public double ChildrenCl;

    public double wingMass;

    public double connectionForce;

    public double MAC;

    public double b_2;

    public double midChordSweep;

    public double taperRatio;

    public double surfaceArea;

    public double aspectRatio;

    public double ArSweepScale;

    #region tweakables

    // Get the tweakable window so we can force it to refresh.
    // NathanKell said to grab it from DRE
    UIPartActionWindow _myWindow = null;
    UIPartActionWindow myWindow
    {
        get
        {
            if (_myWindow == null)
            {
                foreach (UIPartActionWindow window in FindObjectsOfType(typeof(UIPartActionWindow)))
                {
                    if (window.part == part) _myWindow = window;
                }
            }
            return _myWindow;
        }
    }

    // Toggle relative thickness scaling
    [KSPEvent(guiName = "Relative Thickness Scaling")]
    public void ThicknessScalingToggleEvent()
    {
        if (IsAttached &&
            this.part.parent != null)
        {
            relativeThicknessScaling = !relativeThicknessScaling;
            SetThicknessScalingEventName();

            // Update part and children
            UpdateAllCopies(true);

            // Force tweakable window to refresh
            if (myWindow != null)
                myWindow.displayDirty = true;
        }
    }
    public void SetThicknessScalingEventName()
    {
        if (relativeThicknessScaling)
            Events["ThicknessScalingToggleEvent"].guiName = "Relative Thickness Scaling";
        else
            Events["ThicknessScalingToggleEvent"].guiName = "Absolute Thickness Scaling";
    }
    public void SetThicknessScalingEventState()
    {
        if (IsAttached &&
            this.part.parent != null &&
            (!this.part.parent.Modules.Contains("WingManipulator") ||
            IgnoreSnapping ||
            doNotParticipateInParentSnapping))
            Events["ThicknessScalingToggleEvent"].guiActiveEditor = true;
        else
            Events["ThicknessScalingToggleEvent"].guiActiveEditor = false;
    }
    public void SetThicknessScalingTypeToRoot()
    {
        // If we're snapping, match relative thickness scaling with root
        if (this.part.parent != null &&
            this.part.parent.Modules.Contains("WingManipulator") &&
            !IgnoreSnapping &&
            !doNotParticipateInParentSnapping)
        {
            relativeThicknessScaling = this.part.parent.Modules.OfType<WingManipulator>().FirstOrDefault().relativeThicknessScaling;

            // Set relative scaling event name
            SetThicknessScalingEventName();
        }
    }

    // Toggle wing data display
    public bool showWingData = false;
    [KSPEvent(guiActiveEditor = true, guiName = "Show Wing Data")]
    public void InfoToggleEvent()
    {
        if (IsAttached &&
            this.part.parent != null)
        {
            showWingData = !showWingData;
            if (showWingData)
                Events["InfoToggleEvent"].guiName = "Hide Wing Data";
            else
                Events["InfoToggleEvent"].guiName = "Show Wing Data";

            // If FAR|NEAR arent present, toggle Cl/Cd
            if (!FARactive && !NEARactive)
            {
                Fields["guiCd"].guiActiveEditor = showWingData;
                Fields["guiCl"].guiActiveEditor = showWingData;
            }

            // If FAR|NEAR are not present, or its a version without wing mass calculations, toggle wing mass
            if ((!FARactive && !NEARactive) || !FARmass)
                Fields["guiWingMass"].guiActive = showWingData;

            // Toggle the rest of the info values
            Fields["wingCost"].guiActiveEditor = showWingData;
            Fields["guiMAC"].guiActiveEditor = showWingData;
            Fields["guiB_2"].guiActiveEditor = showWingData;
            Fields["guiMidChordSweep"].guiActiveEditor = showWingData;
            Fields["guiTaperRatio"].guiActiveEditor = showWingData;
            Fields["guiSurfaceArea"].guiActiveEditor = showWingData;
            Fields["guiAspectRatio"].guiActiveEditor = showWingData;

            // Force tweakable window to refresh
            if (myWindow != null)
                myWindow.displayDirty = true;
        }
    }

    [KSPEvent(guiName = "Match Taper Ratio")]
    public void MatchTaperEvent()
    {
        // Check for a valid parent
        if (IsAttached &&
            this.part.parent != null &&
            this.part.parent.Modules.Contains("WingManipulator"))
        {
            // Get parents taper
            float parentTaper = (float)this.part.parent.Modules.OfType<WingManipulator>().FirstOrDefault().taperRatio;

            // Scale the tip
            tipScale.Set(
                Mathf.Clamp(((rootScale.x + 1) * parentTaper) - 1, -1, float.MaxValue),
                Mathf.Clamp(((rootScale.y + 1) * parentTaper) - 1, -1, float.MaxValue),
                tipScale.z);

            // Update part and children
            UpdateAllCopies(true);
        }
    }

    [KSPField(guiActiveEditor = false, guiName = "Coefficient of Drag", guiFormat = "F3")]
    public float guiCd;

    [KSPField(guiActiveEditor = false, guiName = "Coefficient of Lift", guiFormat = "F3")]
    public float guiCl;

    [KSPField(guiActiveEditor = false, guiName = "Mass", guiFormat = "F3", guiUnits = "t")]
    public float guiWingMass;

    [KSPField(guiActiveEditor = false, guiName = "Cost")]
    public float wingCost;

    [KSPField(guiActiveEditor = false, guiName = "Mean Aerodynamic Chord", guiFormat = "F3", guiUnits = "m")]
    public float guiMAC;

    [KSPField(guiActiveEditor = false, guiName = "Semi-Span", guiFormat = "F3", guiUnits = "m")]
    public float guiB_2;

    [KSPField(guiActiveEditor = false, guiName = "Mid-Chord Sweep", guiFormat = "F3", guiUnits = "deg.")]
    public float guiMidChordSweep;

    [KSPField(guiActiveEditor = false, guiName = "Taper Ratio", guiFormat = "F3")]
    public float guiTaperRatio;

    [KSPField(guiActiveEditor = false, guiName = "Surface Area", guiFormat = "F3", guiUnits = "m²")]
    public float guiSurfaceArea;

    [KSPField(guiActiveEditor = false, guiName = "Aspect Ratio", guiFormat = "F3")]
    public float guiAspectRatio;

    #endregion

    #region configuration

    // Load shit from a config file
    // Cribbed from FAR, with thanks to ferram4
    public void LoadConfiguration()
    {
        if (loadedConfig)
            return;

        foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("PWingsSettings"))
        {
            if (node == null)
                continue;

            if (node.HasValue("keyTranslation"))
                keyTranslation = node.GetValue("keyTranslation");

            if (node.HasValue("keyTipScale"))
                keyTipScale = node.GetValue("keyTipScale");

            if (node.HasValue("keyRootScale"))
                keyRootScale = node.GetValue("keyRootScale");

            if (node.HasValue("moveSpeed"))
                float.TryParse(node.GetValue("moveSpeed"), out moveSpeed);

            if (node.HasValue("scaleSpeed"))
                float.TryParse(node.GetValue("scaleSpeed"), out scaleSpeed);
        }

        loadedConfig = true;
    }

    #endregion

    #region aerodynamics

    // Gather the Cl of all our children for connection strength calculations.
    public void GatherChildrenCl()
    {
        ChildrenCl = 0;

        // Add up the Cl and ChildrenCl of all our children to our ChildrenCl
        foreach (Part p in this.part.children)
        {
            if (p.Modules.Contains("WingManipulator"))
            {
                var child = p.Modules.OfType<WingManipulator>().FirstOrDefault();
                ChildrenCl += child.Cl;
                ChildrenCl += child.ChildrenCl;
            }
        }

        // If parent is a pWing, trickle the call to gather ChildrenCl down to them.
        if (this.part.parent != null && this.part.parent.Modules.Contains("WingManipulator"))
        {
            var Parent = this.part.parent.Modules.OfType<WingManipulator>().FirstOrDefault();
            Parent.GatherChildrenCl();
        }
    }

    protected bool triggerUpdate = false; // if this is true, an update will be done and it set false.
    // this will set the triggerUpdate field true on all wings on the vessel.
    public void TriggerUpdateAllWings()
    {
        List<Part> plist = new List<Part>();
        if (HighLogic.LoadedSceneIsEditor)
            plist = EditorLogic.SortedShipList;
        else
            plist = part.vessel.Parts;
        for (int i = 0; i < plist.Count; i++)
        {
            Part p = plist[i];
            if (p.Modules.Contains("WingManipulator"))
            {
                WingManipulator wing = (WingManipulator)p.Modules["WingManipulator"];
                if ((object)wing != null)
                    wing.triggerUpdate = true;
            }
        }
    }

    // This method calculates part values such as mass, lift, drag and connection forces, as well as all intermediates.
    public void CalculateAerodynamicValues(bool doInteraction = true)
    {
        // Calculate intemediate values
        //print(part.name + ": Calc Aero values");
        b_2 = (double)tipPosition.z - (double)Root.localPosition.z + 1.0;

        MAC = ((double)tipScale.x + (double)rootScale.x + 2.0) * (double)modelChordLenght / 2.0;

        midChordSweep = (MathD.Rad2Deg * Math.Atan(((double)Root.localPosition.x - (double)tipPosition.x) / b_2));

        taperRatio = ((double)tipScale.x + 1.0) / ((double)rootScale.x + 1.0);

        surfaceArea = MAC * b_2;

        aspectRatio = 2.0 * b_2 / MAC;

        ArSweepScale = Math.Pow(aspectRatio / MathD.Cos(MathD.Deg2Rad * midChordSweep), 2.0) + 4.0;
        ArSweepScale = 2.0 + Math.Sqrt(ArSweepScale);
        ArSweepScale = (2.0 * MathD.PI) / ArSweepScale * aspectRatio;

        wingMass = MathD.Clamp((double)massFudgeNumber * surfaceArea * ((ArSweepScale * 2.0) / (3.0 + ArSweepScale)) * ((1.0 + taperRatio) / 2), 0.01, double.MaxValue);

        Cd = (double)dragBaseValue / ArSweepScale * (double)dragMultiplier;

        Cl = (double)liftFudgeNumber * surfaceArea * ArSweepScale;

        //print("Gather Children");
        GatherChildrenCl();

        connectionForce = MathD.Round(MathD.Clamp(MathD.Sqrt(Cl + ChildrenCl) * (double)connectionFactor, (double)connectionMinimum, double.MaxValue));

        // Values always set
        if (isWing)
        {
            wingCost = (float)wingMass * (1f + (float)ArSweepScale / 4f) * costDensity;
            wingCost = Mathf.Round(wingCost / 5f) * 5f;
        }
        else if (isCtrlSrf)
        {
            wingCost = (float)wingMass * (1f + (float)ArSweepScale / 4f) * costDensity * (1f - modelControlSurfaceFraction);
            wingCost += (float)wingMass * (1f + (float)ArSweepScale / 4f) * costDensityControl * modelControlSurfaceFraction;
            wingCost = Mathf.Round(wingCost / 5f) * 5f;
        }

        part.breakingForce = Mathf.Round((float)connectionForce);
        part.breakingTorque = Mathf.Round((float)connectionForce);

        // Stock-only values
        if ((!FARactive && !NEARactive) || !FARmass)
            part.mass = Mathf.Round((float)wingMass * 100f) / 100f;
        //print("FAR");
        if (!FARactive && !NEARactive)
        {
            if (isWing == true)
            {
                ((Winglet)this.part).deflectionLiftCoeff = Mathf.Round((float)Cl * 100f) / 100f;
                ((Winglet)this.part).dragCoeff = Mathf.Round((float)Cd * 100f) / 100f;
            }

            if (isCtrlSrf == true)
            {
                var mCtrlSrf = part.Modules.OfType<ModuleControlSurface>().FirstOrDefault();
                if ((object)mCtrlSrf != null)
                {
                    mCtrlSrf.deflectionLiftCoeff = Mathf.Round((float)Cl * 100f) / 100f;
                    //mCtrlSrf.dragCoeff = Mathf.Round((float)Cd * 100f) / 100f;
                    mCtrlSrf.ctrlSurfaceArea = modelControlSurfaceFraction;
                }
            }
        }

        // FAR values
        // With reflection stuff from r4m0n
        if (FARactive || NEARactive)
        {
            if (part.Modules.Contains("FARControllableSurface"))
            {
                PartModule FARmodule = part.Modules["FARControllableSurface"];
                Type FARtype = FARmodule.GetType();
                FARtype.GetField("b_2").SetValue(FARmodule, b_2);
                FARtype.GetField("MAC").SetValue(FARmodule, MAC);
                FARtype.GetField("S").SetValue(FARmodule, surfaceArea);
                FARtype.GetField("MidChordSweep").SetValue(FARmodule, midChordSweep);
                FARtype.GetField("TaperRatio").SetValue(FARmodule, taperRatio);
                FARtype.GetField("ctrlSurfFrac").SetValue(FARmodule, modelControlSurfaceFraction);
                //print("Set fields");
                if (doInteraction)
                {
                    if (FARactive)
                        FARtype.GetMethod("StartInitialization").Invoke(FARmodule, null);
                    else if (NEARactive)
                        FARtype.GetMethod("Start").Invoke(FARmodule, null);
                }
            }
            else if (part.Modules.Contains("FARWingAerodynamicModel"))
            {
                PartModule FARmodule = part.Modules["FARWingAerodynamicModel"];
                Type FARtype = FARmodule.GetType();
                FARtype.GetField("b_2").SetValue(FARmodule, b_2);
                FARtype.GetField("MAC").SetValue(FARmodule, MAC);
                FARtype.GetField("S").SetValue(FARmodule, surfaceArea);
                FARtype.GetField("MidChordSweep").SetValue(FARmodule, midChordSweep);
                FARtype.GetField("TaperRatio").SetValue(FARmodule, taperRatio);
                if (doInteraction)
                {
                    if (FARactive)
                        FARtype.GetMethod("StartInitialization").Invoke(FARmodule, null);
                    else if (NEARactive)
                        FARtype.GetMethod("Start").Invoke(FARmodule, null);
                }
            }
            if (!triggerUpdate && doInteraction)
                TriggerUpdateAllWings();
            if(doInteraction)
                triggerUpdate = false;
        }
        //print("FAR Done");
        // Update GUI values
        if (!FARactive && !NEARactive)
        {
            guiCd = Mathf.Round((float)Cd * 100f) / 100f;
            guiCl = Mathf.Round((float)Cl * 100f) / 100f;
        }
        if ((!FARactive && !NEARactive) || !FARmass)
            guiWingMass = part.mass;
        guiMAC = (float)MAC;
        guiB_2 = (float)b_2;
        guiMidChordSweep = (float)midChordSweep;
        guiTaperRatio = (float)taperRatio;
        guiSurfaceArea = (float)surfaceArea;
        guiAspectRatio = (float)aspectRatio;
        if(HighLogic.LoadedSceneIsEditor)
            GameEvents.onEditorShipModified.Fire (EditorLogic.fetch.ship);
    }

    #endregion

    #region Common Methods

    // Print debug values when 'O' is pressed.
    public void DebugValues()
    {
        if (Input.GetKeyDown(KeyCode.O))
        {
            print("updatedRootScaleZ " + updatedRootScaleZ);
            print("updatedTipScaleZ " + updatedTipScaleZ);
            print("tipScaleModified " + tipScaleModified);
            print("rootScaleModified " + rootScaleModified);
            print("isControlSurface " + isCtrlSrf);
            print("DoNotParticipateInParentSnapping " + doNotParticipateInParentSnapping);
            print("IgnoreSnapping " + IgnoreSnapping);
            print("SegmentRoot " + SegmentRoot);
            print("IsAttached " + IsAttached);
            print("Mass " + wingMass);
            print("ConnectionForce " + connectionForce);
            print("DeflectionLift " + Cl);
            print("ChildrenDeflectionLift " + ChildrenCl);
            print("DeflectionDrag " + Cd);
            print("Aspectratio " + aspectRatio);
            print("ArSweepScale " + ArSweepScale);
            print("Surfacearea " + surfaceArea);
            print("taperRatio " + taperRatio);
            print("MidChordSweep " + midChordSweep);
            print("MAC " + MAC);
            print("b_2 " + b_2);
            print("FARactive " + FARactive);
            print("NEARactive " + NEARactive);
        }
    }

    public void SetupCollider()
    {
        baked = new Mesh();
        wingSMR.BakeMesh(baked);
        wingSMR.enabled = false;

        if (transform.GetComponent<MeshCollider>() == null)
            transform.gameObject.AddComponent<MeshCollider>();

        transform.GetComponent<MeshCollider>().sharedMesh = null;
        transform.GetComponent<MeshCollider>().sharedMesh = baked;
        transform.GetComponent<MeshCollider>().convex = true;
        if (FARactive || NEARactive)
        {
            CalculateAerodynamicValues(false);
            if (FARactive)
            {
                if (part.Modules.Contains("FARControllableSurface"))
                {
                    PartModule FARmodule = part.Modules["FARControllableSurface"];
                    Type FARtype = FARmodule.GetType();
                    if(!NEARactive)
                        FARtype.GetMethod("TriggerPartColliderUpdate").Invoke(FARmodule, null);
                }
                else if (part.Modules.Contains("FARWingAerodynamicModel"))
                {
                    PartModule FARmodule = part.Modules["FARWingAerodynamicModel"];
                    Type FARtype = FARmodule.GetType();
                    FARtype.GetMethod("TriggerPartColliderUpdate").Invoke(FARmodule, null);
                }
            }
        }
    }

    public float GetModuleCost(float defaultCost)
    {
        return wingCost;
    }

    public void UpdatePositions()
    {
        cachedrootThicknessMod = rootThicknessMod;
        cachedtipthicknessMod = tipThicknessMod;

        // If we're snapping, match relative thickness scaling with root
        SetThicknessScalingTypeToRoot();
        if (relativeThicknessScaling)
        {
            updatedRootScaleZ = rootThicknessMod * (rootScale.z + 1f);
            updatedTipScaleZ = tipThicknessMod * (tipScale.z + 1f);
        }
        else
        {
            updatedRootScaleZ = rootThicknessMod;
            updatedTipScaleZ = tipThicknessMod;
        }

        tipScaleModified = new Vector3(tipScale.x + 1f, tipScale.y + 1f, updatedTipScaleZ);
        rootScaleModified = new Vector3(rootScale.x + 1f, rootScale.y + 1f, updatedRootScaleZ);

        Tip.localScale = tipScaleModified;
        Root.localScale = rootScaleModified;

        Tip.localPosition = tipPosition + TipSpawnOffset;

        if (IsAttached &&
            this.part.parent != null &&
            this.part.parent.Modules.Contains("WingManipulator") &&
            !IgnoreSnapping &&
            !doNotParticipateInParentSnapping)
        {
            var Parent = this.part.parent.Modules.OfType<WingManipulator>().FirstOrDefault();
            if (this.part.transform.position != Parent.Tip.position)
            {
                this.part.transform.position = Parent.Tip.position;
            }
            if (rootScale != Parent.tipScale)
                rootScale = Parent.tipScale;
            if (rootThicknessMod != Parent.tipThicknessMod)
                rootThicknessMod = Parent.tipThicknessMod;
        }

        if (symmetricMovement == false)
        {
            tipPosition.y = Root.localPosition.y;
        }
        else
        {
            tipPosition.y = 0f;
            tipPosition.x = 0f;
            rootPosition.x = 0f;
            rootPosition.y = 0f;

            Root.localPosition = -tipPosition + -TipSpawnOffset;
        }
    }

    public void UpdateAllCopies(bool childrenNeedUpdate)
    {
        UpdatePositions();
        SetupCollider();

        if (updateChildren && childrenNeedUpdate)
            UpdateChildren();

        if (isWing || isCtrlSrf )
            CalculateAerodynamicValues();

        foreach (Part p in this.part.symmetryCounterparts)
        {
            var clone = p.Modules.OfType<WingManipulator>().FirstOrDefault();

            clone.rootScale = rootScale;
            clone.tipScale = tipScale;
            clone.tipPosition = tipPosition;

            clone.relativeThicknessScaling = relativeThicknessScaling;
            clone.SetThicknessScalingEventName();

            clone.UpdatePositions();
            clone.SetupCollider();

            if (updateChildren && childrenNeedUpdate)
                clone.UpdateChildren();

            if (isWing || isCtrlSrf)
                clone.CalculateAerodynamicValues();
        }
    }

    // Updates child pWings
    public void UpdateChildren()
    {
        // Get the list of child parts
        foreach (Part p in this.part.children)
        {
            // Check that it is a pWing and that it is affected by parent snapping
            if (p.Modules.Contains("WingManipulator") &&
                !p.Modules.OfType<WingManipulator>().FirstOrDefault().IgnoreSnapping &&
                !p.Modules.OfType<WingManipulator>().FirstOrDefault().doNotParticipateInParentSnapping)
            {
                // get the pWing module
                var child = p.Modules.OfType<WingManipulator>().FirstOrDefault();

                // Update its positions and refresh the collider
                child.UpdatePositions();
                child.SetupCollider();

                // If its a wing, refresh its aerodynamic values
                if (isWing || isCtrlSrf) // FIXME should this be child.isWing etc?
                    child.CalculateAerodynamicValues();
            }
        }
    }

    // Fires when the part is attached
    public void UpdateOnEditorAttach()
    {
        // We are attached
        IsAttached = true;

        // If we were the root of a detached segment, check for the mouse state
        // and set snap override.
        if (SegmentRoot)
        {
            IgnoreSnapping = Input.GetKey(KeyCode.Mouse1);
            SegmentRoot = false;
        }

        // If we're snapping, match relative thickness scaling type with root
        SetThicknessScalingTypeToRoot();

        // if snap is not ignored, lets update our dimensions.
        if (this.part.parent != null &&
            this.part.parent.Modules.Contains("WingManipulator") &&
            !IgnoreSnapping &&
            !doNotParticipateInParentSnapping)
        {
            UpdatePositions();
            SetupCollider();
        }

        // Now redo aerodynamic values.
        if (isWing || isCtrlSrf)
            CalculateAerodynamicValues();

        // Enable root-matching events
        if (this.part.parent != null &&
            this.part.parent.Modules.Contains("WingManipulator") &&
            !IgnoreSnapping &&
            !doNotParticipateInParentSnapping)
        {
            Events["MatchTaperEvent"].guiActiveEditor = true;
        }

        // Enable relative scaling event
        SetThicknessScalingEventState();
    }

    private Callback CachedOnEditorAttach;

    public void UpdateOnEditorDetach()
    {
        // If the root is not null and is a pWing, set its justDetached so it knows to check itself next Update
        if (this.part.parent != null && this.part.parent.Modules.Contains("WingManipulator"))
            this.part.parent.Modules.OfType<WingManipulator>().FirstOrDefault().justDetached = true;

        // We are not attached.
        IsAttached = false;
        justDetached = true;

        // Disable root-matching events
        Events["MatchTaperEvent"].guiActiveEditor = false;

        // Disable relative scaling event
        SetThicknessScalingEventState();
    }

    private Callback CachedOnEditorDetach;
    
    #endregion

    #region PartModule

    public override void OnAwake()
    {
        base.OnAwake();
        LoadConfiguration();
    }

    private void Setup(bool doInteraction)
    {
        FARactive = AssemblyLoader.loadedAssemblies.Any(a => a.assembly.GetName().Name.Equals("FerramAerospaceResearch", StringComparison.InvariantCultureIgnoreCase));
        NEARactive = AssemblyLoader.loadedAssemblies.Any(a => a.assembly.GetName().Name.Equals("NEAR", StringComparison.InvariantCultureIgnoreCase));

        Tip = part.FindModelTransform("Tip");
        Root = part.FindModelTransform("Root");
        SMRcontainer = part.FindModelTransform("Collider");
        wingSMR = SMRcontainer.GetComponent<SkinnedMeshRenderer>();

        UpdatePositions();
        SetupCollider();

        scaleMultipleTip.Set(1, 1, 1);
        scaleMultipleRoot.Set(1, 1, 1);

        if (isWing || isCtrlSrf)
            CalculateAerodynamicValues(doInteraction);

        cachedrootThicknessMod = rootThicknessMod;
        cachedtipthicknessMod = tipThicknessMod;

        // If FAR|NEAR are present, disable the Cl/Cd editor info
        if (FARactive || NEARactive)
        {
            // If FAR|NEAR have the "massPerWingAreaSupported" value, disable mass calculations, and the mass editor info.
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("FARAeroData"))
            {
                if (node == null)
                    continue;

                if (node.HasValue("massPerWingAreaSupported"))
                    FARmass = true;
            }
        }

        // Enable root-matching events
        if (IsAttached &&
            this.part.parent != null &&
            this.part.parent.Modules.Contains("WingManipulator"))
        {
            Events["MatchTaperEvent"].guiActiveEditor = true;
        }

        // Set active state of relative scaling event
        SetThicknessScalingEventState();
        // Set relative scaling event name
        SetThicknessScalingEventName();
    }

    public override void OnStart(StartState state)
    {
        base.OnStart(state);

        Setup(true);
    }

    public override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);
    }

    public void Update()
    {
        if (HighLogic.LoadedSceneIsEditor && wingSMR != null)
        {
            //Sets the skinned meshrenderer to update even when culled for being outside the screen
            if (wingSMR.updateWhenOffscreen != true)
                wingSMR.updateWhenOffscreen = true;
            
            // Add delegates to editor attach/detach events
            if (CachedOnEditorAttach == null)
                CachedOnEditorAttach = new Callback(UpdateOnEditorAttach);
            if (!this.part.OnEditorAttach.GetInvocationList().Contains(CachedOnEditorAttach))
                this.part.OnEditorAttach += CachedOnEditorAttach;

            if (CachedOnEditorDetach == null)
                CachedOnEditorDetach = new Callback(UpdateOnEditorDetach);
            if (!this.part.OnEditorDetach.GetInvocationList().Contains(CachedOnEditorDetach))
                this.part.OnEditorDetach += CachedOnEditorDetach;

            // A pWing has just detached from us, or we have just detached
            if (justDetached)
            {
                if (!IsAttached)
                {
                    // We have just detached. Check if we're the root of the detached segment
                    SegmentRoot = (this.part.parent == null) ? true : false;
                }
                else
                {
                    // A pWing just detached from us, we need to redo the wing values.
                    if (isWing || isCtrlSrf)
                        CalculateAerodynamicValues();
                }

                // And set this to false so we only do it once.
                justDetached = false;
            }

            // Check if the root's relative thickness scaling has changed if applicable
            var cachedRelativeThicknessScaling = relativeThicknessScaling;
            SetThicknessScalingTypeToRoot();

            // Check if thickness mods have changed, and if so update us and any children
            if (IsAttached &&
                (tipThicknessMod != cachedtipthicknessMod ||
                rootThicknessMod != cachedrootThicknessMod ||
                cachedRelativeThicknessScaling != relativeThicknessScaling))
            {
                UpdateAllCopies(true);
            }
            if (triggerUpdate)
                CalculateAerodynamicValues();
        }
    }

    void OnMouseOver()
    {

        if (HighLogic.LoadedSceneIsFlight)
        {
            // If we're in flight, do nothing.
            // Except allow debug print
            DebugValues();
        }
        else if (HighLogic.LoadedSceneIsEditor)
        {

            // We're in editor. Allow scaling with mouse.
            // Let's check that the parent is not null, we're attached to something, and one of the keys is down.
            if (this.part.parent != null &&
                IsAttached &&
                (Input.GetKey(keyTipScale) ||
                Input.GetKey(keyRootScale) ||
                Input.GetKey(keyTranslation)))
            {
                bool childrenNeedUpdate = false;

                // Translation
                if (Input.GetKey(keyTranslation))
                {
                    if (symmetricMovement == true)
                    {
                        // Symmetric movement (for wing edge control surfaces)
                        tipPosition += (Input.GetAxis("Mouse X") * (GameObject.Find("Main Camera").transform.up) * TimeWarp.deltaTime * -moveSpeed);
                        tipPosition += (Input.GetAxis("Mouse Y") * (GameObject.Find("Main Camera").transform.right) * TimeWarp.deltaTime * moveSpeed);
                        // Clamp x, y at 0 to prevent transient offsets
                        // Clamp z to modelMinimumSpan/2 to prevent turning the model inside-out
                        tipPosition.Set(
                            0,
                            0,
                            Mathf.Clamp(tipPosition.z, modelMinimumSpan/2-TipSpawnOffset.z, float.MaxValue));

                        rootPosition += (Input.GetAxis("Mouse X") * (-GameObject.Find("Main Camera").transform.up) * TimeWarp.deltaTime * -moveSpeed);
                        rootPosition += (Input.GetAxis("Mouse Y") * (-GameObject.Find("Main Camera").transform.right) * TimeWarp.deltaTime * moveSpeed);
                        // Clamp x, y at 0 to prevent transient offsets
                        // Clamp z to modelMinimumSpan/2 to prevent turning the model inside-out
                        rootPosition.Set(
                            0,
                            0,
                            Mathf.Clamp(rootPosition.z, modelMinimumSpan/2-TipSpawnOffset.z, float.MaxValue));
                    }
                    else
                    {
                        // Normal, only tip moves
                        tipPosition += (Input.GetAxis("Mouse X") * (GameObject.Find("Main Camera").transform.up) * TimeWarp.deltaTime * -moveSpeed);
                        tipPosition += (Input.GetAxis("Mouse Y") * (GameObject.Find("Main Camera").transform.right) * TimeWarp.deltaTime * moveSpeed);
                        // Clamp y at 0 to prevent transient height offsets
                        // Clamp z to modelMinimumSpan to prevent turning the model inside-out
                        tipPosition.Set(
                            tipPosition.x,
                            0,
                            Mathf.Clamp(tipPosition.z, modelMinimumSpan-TipSpawnOffset.z, float.MaxValue));
                    }

                    // If this part updates its children, make sure they are
                    childrenNeedUpdate = true;
                }

                // Tip scaling
                if (Input.GetKey(keyTipScale))
                {
                    tipScale += (Input.GetAxis("Mouse Y") * scaleSpeed * scaleMultipleTip);
                    // Clamp scale values to -1 to prevent hourglass wings
                    tipScale.Set(
                        Mathf.Clamp(tipScale.x, -1, float.MaxValue),
                        Mathf.Clamp(tipScale.y, -1, float.MaxValue),
                        tipScale.z);

                    // If this part updates its children, make sure they are
                    childrenNeedUpdate = true;
                }

                // Root scaling
                // only if the root part is not a pWing,
                // or we were told to ignore snapping,
                // or the part is set to ignore snapping (wing edge control surfaces, tipically)
                if (Input.GetKey(keyRootScale) && 
                    (!this.part.parent.Modules.Contains("WingManipulator") ||
                    IgnoreSnapping ||
                    doNotParticipateInParentSnapping))
                {
                    rootScale += (Input.GetAxis("Mouse Y") * scaleSpeed * scaleMultipleRoot);
                    // Clamp scale values to -1 to prevent hourglass wings
                    rootScale.Set(
                        Mathf.Clamp(rootScale.x, -1, float.MaxValue),
                        Mathf.Clamp(rootScale.y, -1, float.MaxValue),
                        rootScale.z);
                }

                // Done scaling & moving. Update us and any symmetry couterparts.
                UpdateAllCopies(childrenNeedUpdate);
            }

            // Print debug on mouseover
            DebugValues();
        }
    }

    // Stock wing lift value query
    public void OnCenterOfLiftQuery(CenterOfLiftQuery qry)
    {
        if (IsAttached && !FARactive)
        {
            qry.lift = (float)Cl;
        }
    }

    #endregion
}


