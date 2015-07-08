using System;
using System.Collections.Generic;
using System.Collections;
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
            if (!FARactive)
            {
                Fields["guiCd"].guiActiveEditor = showWingData;
                Fields["guiCl"].guiActiveEditor = showWingData;
            }

            // If FAR|NEAR are not present, or its a version without wing mass calculations, toggle wing mass
            if (!FARactive)
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
            WingManipulator child = p.Modules.OfType<WingManipulator>().FirstOrDefault();
            if (child != null)
            {
                ChildrenCl += child.Cl;
                ChildrenCl += child.ChildrenCl;
            }
        }

        // If parent is a pWing, trickle the call to gather ChildrenCl down to them.
        if (this.part.parent != null)
        {
            WingManipulator Parent = this.part.parent.Modules.OfType<WingManipulator>().FirstOrDefault();
            if (Parent != null)
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
            WingManipulator wing = plist[i].Modules.OfType<WingManipulator>().FirstOrDefault();
            if (wing != null)
                wing.triggerUpdate = true;
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
        if (!FARactive)
        {
            // numbers for lift from: http://forum.kerbalspaceprogram.com/threads/118839-Updating-Parts-to-1-0?p=1896409&viewfull=1#post1896409
            float stockLiftCoefficient = (float)(surfaceArea / 3.52);
            // CoL/P matches CoM unless otherwise specified
            part.CoMOffset = new Vector3(Vector3.Dot(Tip.position - Root.position, part.transform.right) / 2, Vector3.Dot(Tip.position - Root.position, part.transform.up) / 2, 0);
            if (isWing && !isCtrlSrf)
            {
                part.Modules.GetModules<ModuleLiftingSurface>().FirstOrDefault().deflectionLiftCoeff = stockLiftCoefficient;
                part.mass = stockLiftCoefficient * 0.1f;
            }
            else
            {
                ModuleControlSurface mCtrlSrf = part.Modules.OfType<ModuleControlSurface>().FirstOrDefault();
                if (mCtrlSrf != null)
                {
                    mCtrlSrf.deflectionLiftCoeff = stockLiftCoefficient;
                    mCtrlSrf.ctrlSurfaceArea = modelControlSurfaceFraction;
                    part.mass = stockLiftCoefficient * (1 + modelControlSurfaceFraction) * 0.1f;
                }
            }
        }

        // FAR values
        // With reflection stuff from r4m0n
        if (FARactive)
        {
            if (part.Modules.Contains("FARControllableSurface"))
            {
                PartModule FARmodule = part.Modules["FARControllableSurface"];
                Type FARtype = FARmodule.GetType();
                FARtype.GetField("b_2").SetValue(FARmodule, b_2);
                FARtype.GetField("b_2_actual").SetValue(FARmodule, b_2);
                FARtype.GetField("MAC").SetValue(FARmodule, MAC);
                FARtype.GetField("MAC_actual").SetValue(FARmodule, MAC);
                FARtype.GetField("S").SetValue(FARmodule, surfaceArea);
                FARtype.GetField("MidChordSweep").SetValue(FARmodule, midChordSweep);
                FARtype.GetField("TaperRatio").SetValue(FARmodule, taperRatio);
                FARtype.GetField("ctrlSurfFrac").SetValue(FARmodule, modelControlSurfaceFraction);
                //print("Set fields");
                if (doInteraction && FARactive)
                {
                    FARtype.GetMethod("StartInitialization").Invoke(FARmodule, null);
                    part.SendMessage("GeometryPartModuleRebuildMeshData"); // notify FAR that geometry has changed
                }
            }
            else if (part.Modules.Contains("FARWingAerodynamicModel"))
            {
                PartModule FARmodule = part.Modules["FARWingAerodynamicModel"];
                Type FARtype = FARmodule.GetType();
                FARtype.GetField("b_2").SetValue(FARmodule, b_2);
                FARtype.GetField("b_2_actual").SetValue(FARmodule, b_2);
                FARtype.GetField("MAC").SetValue(FARmodule, MAC);
                FARtype.GetField("MAC_actual").SetValue(FARmodule, MAC);
                FARtype.GetField("S").SetValue(FARmodule, surfaceArea);
                FARtype.GetField("MidChordSweep").SetValue(FARmodule, midChordSweep);
                FARtype.GetField("TaperRatio").SetValue(FARmodule, taperRatio);
                if (doInteraction && FARactive)
                {
                    FARtype.GetMethod("StartInitialization").Invoke(FARmodule, null);
                    part.SendMessage("GeometryPartModuleRebuildMeshData"); // notify FAR that geometry has changed
                }
            }
            if (!triggerUpdate && doInteraction)
                TriggerUpdateAllWings();
            if(doInteraction)
                triggerUpdate = false;
        }
        //print("FAR Done");
        // Update GUI values
        if (!FARactive)
        {
            guiCd = Mathf.Round((float)Cd * 100f) / 100f;
            guiCl = Mathf.Round((float)Cl * 100f) / 100f;
            guiWingMass = part.mass;
            StartCoroutine(updateDragCube());
        }
            
        guiMAC = (float)MAC;
        guiB_2 = (float)b_2;
        guiMidChordSweep = (float)midChordSweep;
        guiTaperRatio = (float)taperRatio;
        guiSurfaceArea = (float)surfaceArea;
        guiAspectRatio = (float)aspectRatio;
        if(HighLogic.LoadedSceneIsEditor)
            GameEvents.onEditorShipModified.Fire (EditorLogic.fetch.ship);
    }

    float updateTimeDelay = 0;
    IEnumerator updateDragCube()
    {
        bool running = updateTimeDelay > 0;
        updateTimeDelay = 0.5f;
        if (running)
            yield break;
        while (updateTimeDelay > 0)
        {
            updateTimeDelay -= TimeWarp.deltaTime;
            yield return null;
        }
        DragCube DragCube = DragCubeSystem.Instance.RenderProceduralDragCube(part);
        part.DragCubes.ClearCubes();
        part.DragCubes.Cubes.Add(DragCube);
        part.DragCubes.ResetCubeWeights();
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
        }
    }

    public void SetupCollider()
    {
        baked = new Mesh();
        wingSMR.BakeMesh(baked);
        wingSMR.enabled = false;
        Transform modelTransform = transform.FindChild("model");
        if (modelTransform.GetComponent<MeshCollider>() == null)
            modelTransform.gameObject.AddComponent<MeshCollider>();
        MeshCollider meshCol = modelTransform.GetComponent<MeshCollider>();
        meshCol.sharedMesh = null;
        meshCol.sharedMesh = baked;
        meshCol.convex = true;
        if (FARactive)
        {
            CalculateAerodynamicValues(false);
            PartModule FARmodule = null;
            if (part.Modules.Contains("FARControllableSurface"))
                FARmodule = part.Modules["FARControllableSurface"];
            else if (part.Modules.Contains("FARWingAerodynamicModel"))
                FARmodule = part.Modules["FARWingAerodynamicModel"];
            if (FARmodule != null)
            {
                Type FARtype = FARmodule.GetType();
                FARtype.GetMethod("TriggerPartColliderUpdate").Invoke(FARmodule, null);
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
            WingManipulator wing = p.Modules.OfType<WingManipulator>().FirstOrDefault();
            if (wing != null && !wing.IgnoreSnapping && !wing.doNotParticipateInParentSnapping)
            {
                // Update its positions and refresh the collider
                wing.UpdatePositions();
                wing.SetupCollider();

                // If its a wing, refresh its aerodynamic values
                if (isWing || isCtrlSrf) // FIXME should this be child.isWing etc?
                    wing.CalculateAerodynamicValues();
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
            Events["MatchTaperEvent"].guiActiveEditor = true;
        }

        // Now redo aerodynamic values.
        if (isWing || isCtrlSrf)
            CalculateAerodynamicValues();

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
        if (!HighLogic.LoadedSceneIsEditor || wingSMR == null)
            return;

        DeformWing();

        //Sets the skinned meshrenderer to update even when culled for being outside the screen
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

    Vector3 lastMousePos;
    int state = 0; // 0 == nothing, 1 == translate, 2 == tipScale, 3 == rootScale
    public void DeformWing()
    {
        if (this.part.parent == null || !IsAttached || state == 0)
            return;
        
        float depth = EditorCamera.Instance.camera.WorldToScreenPoint(state != 3 ? Tip.position : Root.position).z; // distance of tip transform from camera
        Vector3 diff = (state == 1 ? moveSpeed : scaleSpeed * 20) * depth * (Input.mousePosition - lastMousePos) / 4500;
        lastMousePos = Input.mousePosition;

        // Translation
        if (state == 1)
        {
            if (!Input.GetKey(keyTranslation))
            {
                state = 0;
                return;
            }

            if (symmetricMovement == true)
            { // Symmetric movement (for wing edge control surfaces)
                tipPosition.z -= diff.x * Vector3.Dot(EditorCamera.Instance.camera.transform.right, part.transform.right) + diff.y * Vector3.Dot(EditorCamera.Instance.camera.transform.up, part.transform.right);
                tipPosition.z = Mathf.Max(tipPosition.z, modelMinimumSpan / 2 - TipSpawnOffset.z); // Clamp z to modelMinimumSpan/2 to prevent turning the model inside-out

                rootPosition.z += diff.x * Vector3.Dot(EditorCamera.Instance.camera.transform.right, part.transform.right) + diff.y * Vector3.Dot(EditorCamera.Instance.camera.transform.up, part.transform.right);
                rootPosition.z = Mathf.Max(rootPosition.z, modelMinimumSpan / 2 - TipSpawnOffset.z); // Clamp z to modelMinimumSpan/2 to prevent turning the model inside-out
            }
            else
            { // Normal, only tip moves
                tipPosition.x += diff.x * Vector3.Dot(EditorCamera.Instance.camera.transform.right, part.transform.up) + diff.y * Vector3.Dot(EditorCamera.Instance.camera.transform.up, part.transform.up);
                tipPosition.z += diff.x * Vector3.Dot(EditorCamera.Instance.camera.transform.right, part.transform.right) + diff.y * Vector3.Dot(EditorCamera.Instance.camera.transform.up, part.transform.right);
                tipPosition.z = Mathf.Max(tipPosition.z, modelMinimumSpan - TipSpawnOffset.z); // Clamp z to modelMinimumSpan to prevent turning the model inside-out
            }
            UpdateAllCopies(true);
        }
        // Tip scaling
        else if (state == 2)
        {
            if (!Input.GetKey(keyTipScale))
            {
                state = 0;
                return;
            }
            float scaleDiff = diff.x * Vector3.Dot(EditorCamera.Instance.camera.transform.right, part.transform.up) + diff.y * Vector3.Dot(EditorCamera.Instance.camera.transform.up, part.transform.up);
            tipScale.Set(Mathf.Max(tipScale.x + scaleDiff, 0), Mathf.Max(tipScale.y + scaleDiff, 0), tipScale.z + scaleDiff); // Clamp scale values to 0 to prevent hourglass wings
            UpdateAllCopies(true);
        }
        // Root scaling
        // only if the root part is not a pWing,
        // or we were told to ignore snapping,
        // or the part is set to ignore snapping (wing edge control surfaces, tipically)
        else if (state == 3 && (!this.part.parent.Modules.Contains("WingManipulator") || IgnoreSnapping || doNotParticipateInParentSnapping))
        {
            if (!Input.GetKey(keyRootScale))
            {
                state = 0;
                return;
            }
            float scaleDiff = diff.x * Vector3.Dot(EditorCamera.Instance.camera.transform.right, part.transform.up) + diff.y * Vector3.Dot(EditorCamera.Instance.camera.transform.up, part.transform.up);
            rootScale.Set(Mathf.Max(rootScale.x + scaleDiff, -1), Mathf.Max(rootScale.y + scaleDiff, -1), rootScale.z + scaleDiff);
            UpdateAllCopies(false);
        }        
    }

    void OnMouseOver()
    {
        DebugValues();
        if (!HighLogic.LoadedSceneIsEditor || state != 0)
            return;

        lastMousePos = Input.mousePosition;
        if (Input.GetKeyDown(keyTranslation))
            state = 1;
        else if (Input.GetKeyDown(keyTipScale))
            state = 2;
        else if (Input.GetKeyDown(keyRootScale))
            state = 3;
    }
    #endregion
}