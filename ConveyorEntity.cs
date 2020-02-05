#define PROFILE_CONVEYORSx
using UnityEngine;
using System.Collections;

//To consider; if conveyors carry bars 99% of the time, pre-instantiate those and use UVs, and instantiate a rarer, tertiary object

public class ConveyorEntity : MachineEntity, PowerConsumerInterface, ItemSupplierInterface, ItemConsumerInterface
{
	public const float TIER1_SPEED = 1;
	public const float TIER2_SPEED = 2;
	public const float TIER3_SPEED = 4;
	public const float TIER0_SPEED = 0.15f;
	public const float FAST_SETTING_SPEED_FACTOR = 2;
	public const float VERTICAL_SPEED_FACTOR = 1;//was 0.25f;
	public static int PowerPerItem = 10;//theoretical max of 150/face, which is 750/min per face.
	
	public const float CARRY_TIME = 1;
	public const float VISUAL_CARRY_TIME = 1;

	public const float MYNOCK_DEBOUCE_TIME = 600; // Avoid spawning mynocks on the same conveyor for 10 minutes after one is killed.

	public string mText = null;
	//private bool stringDirty;
	
	public float mrMaxPower = 1500.0f;
	public float mrCurrentPower = 0.0f;
	float mrNormalisedPower;
	public float mrPowerSpareCapacity;

	public float mrLockTimer;//Do not increment visual or carry timer; rewind them to .5;set conveyor speed accordingly; (even consider slowing down the conveyor)
	public float mrRobotLockTimer;//Used purely to say 'I have this'; decays to avoid reference count issues
	
	bool mbLinkedToGO;
	public bool mbCheckAdjacencyRotation;

	public bool mbReadyToConvey;
	public float mrCarryTimer;//0-1, updated at 3.3hz (this represents normalised time!)
	public float mrVisualCarryTimer;//0-1, updated at 60hz
	public float mrCarrySpeed = 1.0f;
	public float mrBaseCarrySpeed = 1.0f;//before faffing with cold, etc

	public float mrMynockCounter = 0.0f;
	public float mrMynockDebounce = 0.0f;

	public GameObject mCarriedObjectParent;//this is what we move, and what has the LOD calc on it until Martijn does more useful lodding code
    public GameObject mCarriedObjectCube;//A child of above, has it's UVs set
    public GameObject mCarriedObjectItem;	//A child of the parent
    public GameObject mConveyorObject;//optimise things by predicating off of this objects IsVisible
    public GameObject mBeltObject;//can be hidden at a distance; containers the UVScroller component

	GameObject SpecificDetail;//Used in special cases only
	Animation mAnimation;

	public ushort mCarriedCube;	//Is this now 100% deprecated?
	public ushort mCarriedValue; // ALSO CARRY VALUE FFS
	public ItemBase mCarriedItem;

	public int ExemplarItemID = -1;
	public ushort ExemplarBlockID;
	public ushort ExemplarBlockValue;
	public bool mbInvertExemplar;

	public Light mMotorLight;

	System.Random mRand;

	public Vector3 mItemForwards;//if our item was delivered from another conveyor, then 

	public bool mbMynockNeeded;//if we load off disk and our mynock timer drops below 5, we know we haven't got a saved mynock on there

	public int mnItemsPerMinute;
	public int mnItemsThisMinute;
	float mrIPMCounter = 60;

    bool mbInstancedBase;
    int mnInstancedID = -1;
    int mnInstancerType;

	// Flag to stop grommet exploit.
        private bool mInOffloadFromGrommet;

	public void ClearConveyor()
	{
		mCarriedCube = eCubeTypes.NULL;
		mCarriedItem = null;
		//Reset other things as needed

		mbReadyToConvey = true;
		mrCarryTimer = 0.0f;
		mbStopBelt = true;
		mbConveyorBlocked = false;
		MarkDirtyDelayed();
	}


	public uint mConveyedItems;

	bool mbUnityCubeNeedsUpdate;

	Vector3 mDetailForward = Vector3.forward;
	public Vector3 mForwards;
	public Vector3 mUp;//What we rotate around, if requested
    public Vector3 mLeft;
    public Vector3 mRight;

//	public bool mbAttachedToConveyor;
//	public bool mbAttachedToHopper;
	public bool mbConveyorBlocked;	
	public bool mbStopBelt;//Is this 'stop animating the scroller'
    public float mrBlockedTime;
    public bool mbConveyorVisuallyBlocked;

	public static GameObject IngotObject;

	bool mbRotateModel = false;
	Quaternion mTargetRotation = Quaternion.identity;
	int mnRotationWithoutSuccess;

//	public bool ForceRotationAroundY;

	//This order MUST match the Value!
	public enum eConveyorType
	{
		eConveyor,
		eConveyorFilter,
		eTransportPipe,
		eTransportPipeFilter,

		eConveyorStamper,	//converts bars into plates
		eConveyorExtruder,	//converts bars into piles of wire, somehow
		eConveyorCoiler,	//converts piles of wire into spools of wire, somehow.

		eConveyorBender,	//converts tubes into bent tubes
		eConveyorPipeExtrusion,//converts bars into tubes

		eConveyorPCBAssembler,//converts pipes into bent pipes <-best comment ever

		eConveyorTurntable,//automatically rotates after item entry; cannot rotate twice, only once, three and four times

		eBasicConveyor,//cheap as chips, but really horribly slow

		eAdvancedFilter,//allows exemplar

		eConveyorSlopeUp,//Slopes move an item up and along a square. Act like normal conveyors in all other respects. Basically add our up to our drop off position
		eConveyorSlopeDown,

		eMotorisedConveyor,//This performs the storage hopper look up 3x faster

        eBasicCorner_CW,
        eBasicCorner_CCW,

        eCorner_CW,
        eCorner_CCW,

        eNumConveyorTypes,
	}

	//conversion machines should rely almost entirely on unity animation, and simply offer up the necessary item onwards.
	bool mbItemHasBeenConverted;
	int mnNumRotations;
	public bool mbRecommendPlayerUnfreeze;
	public bool mbConveyorFrozen;//run at 1% speed, turn blue.
	public bool mbConveyorToxic;//run at 0-10% speed, turn green
	int mnPenaltyFactor;//0-100
	bool mbConveyorNeedsColourChange;

	public int mnCurrentPenaltyFactor = 0;

	float mrSleepTimer;

	//This can only be set if we are Value 1/3, which is a Filter
	public eHopperRequestType meRequestType = eHopperRequestType.eAny;
	// ************************************************************************************************************************************************
	public void SwitchRequestType()
	{
		MarkDirtyDelayed();
		meRequestType++;
		if (meRequestType == eHopperRequestType.eNum) meRequestType = eHopperRequestType.eAny;
		ExemplarItemID = -1;
		ExemplarBlockValue = 0;
		ExemplarBlockID = 0;
		ExemplarString = PersistentSettings.GetString("Conv_NONE");
		Debug.LogWarning("Conveyor using generic requests and wiping Exemplar!");
	}
    // ************************************************************************************************************************************************
    public void SwitchRequestTypeRev()
    {
        MarkDirtyDelayed();
        meRequestType--;
		if ((int)meRequestType <= 0) meRequestType = (eHopperRequestType.eNum - 1);
        ExemplarItemID = -1;
        ExemplarBlockValue = 0;
        ExemplarBlockID = 0;
        ExemplarString = PersistentSettings.GetString("Conv_NONE");
        Debug.LogWarning("Conveyor using generic requests and wiping Exemplar!");
    }
	public string ExemplarString = PersistentSettings.GetString("Conv_NONE");
	// ************************************************************************************************************************************************
	public void SetExemplar(ItemBase lExemplar)
	{
        FloatingCombatTextQueue lFQ = FloatingCombatTextManager.instance.QueueText(mnX,mnY+1,mnZ,1.0f,ItemManager.GetItemName(lExemplar),Color.green,1.5f);
        if (lFQ != null) lFQ.mrStartRadiusRand = 0.25f;

		ExemplarString = ItemManager.GetItemName(lExemplar);
		if (lExemplar.mnItemID != -1)
		{
			ExemplarItemID = lExemplar.mnItemID;
			if (ExemplarItemID == 0)
			{
				Debug.LogError("Error, Exemplar attempted to be set, but no ItemID? " + ItemManager.GetItemName(lExemplar));
				meRequestType = eHopperRequestType.eNone; // Illegal request type, prevent anything being extracted.
			}
			else
			{
				// Set to Any so that we don't interfere now a valid examplar has been set.
				meRequestType = eHopperRequestType.eAny;
			}

			ExemplarBlockValue = 0;
			ExemplarBlockID = 0;
		}
		else
		{
			if (lExemplar.mType == ItemType.ItemCubeStack)
			{
				ExemplarBlockID 	= (lExemplar as ItemCubeStack).mCubeType;
				ExemplarBlockValue 	= (lExemplar as ItemCubeStack).mCubeValue;
				ExemplarItemID = -1;

				// Set to Any so that we don't interfere now a valid examplar has been set.
				meRequestType = eHopperRequestType.eAny;
			}
			else
			{
				//I dont fucking know
				Debug.LogWarning("Error, unable to set exemplars for type " + ItemManager.GetItemName(lExemplar));
				meRequestType = eHopperRequestType.eNone; // Illegal request type, prevent anything being extracted.
			}
		}

	
		MarkDirtyDelayed();
		Debug.LogWarning("Conveyor set to Exemplar " + ExemplarItemID +"/" + ExemplarBlockID + "/" + ExemplarBlockValue);
		                 //+ ItemManager.GetItemName(lExemplar));
		                 //lnItemID);
	}
    // ************************************************************************************************************************************************
    public ConveyorEntity(Segment segment, long x, long y, long z, ushort cube, byte flags, ushort lValue, bool lbLoadedFromDisk) : base(eSegmentEntity.Conveyor, SpawnableObjectEnum.Conveyor, x, y, z, cube, flags, lValue, Vector3.zero, segment)
    {
        mRand = new System.Random();
        mValue = lValue;
        SetObjectType();

        mbNeedsLowFrequencyUpdate = true;
        mbNeedsUnityUpdate = true;
        mCarriedCube = eCubeTypes.NULL;
        mrCarryTimer = 0;
        mbReadyToConvey = true;

        mForwards = SegmentCustomRenderer.GetRotationQuaternion(flags) * Vector3.forward;
        mForwards.Normalize();

        mDetailForward = mForwards;

        mItemForwards = mForwards;//Start with a Valid forwards

        mUp = SegmentCustomRenderer.GetRotationQuaternion(flags) * Vector3.up;
        mUp.Normalize();
        mLeft = SegmentCustomRenderer.GetRotationQuaternion(flags) * Vector3.left;
        mLeft.Normalize();
        mRight = SegmentCustomRenderer.GetRotationQuaternion(flags) * Vector3.right;
        mRight.Normalize();

        mrMaxPower = 0;

        base.DoThreadedRaycastVis = true;
        base.MaxRayCastVis = 80;
        base.RayCastOffset = mUp * 0.35f;//new Vector3(0,0.5f,0);//so above the conveyor, not world.up (Reduced so that we won't break 'above' within a single cube)

        if (lValue == (ushort)eConveyorType.eConveyorFilter || lValue == (ushort)eConveyorType.eTransportPipeFilter)
        {
            meRequestType = eHopperRequestType.eNone;//Default to nothing, so player can make the rest of the network
        }

        if (lValue == (ushort)eConveyorType.eAdvancedFilter)
        {
            meRequestType = eHopperRequestType.eNone;//Default to nothing, so player can make the rest of the network; BlockId or non-zero Exemplar will allow progress
        }

        /*
		//T1 conveyors cannot stick to walls and ceilings; their Y must be zero!
		if (lValue == (ushort)eConveyorType.eConveyor || lValue == (ushort)eConveyorType.eConveyorFilter)
		{
			if (mForwards.y != 0.0f)
			{
				WorldScript.instance.Dig(segment,(int)(x % 16),(int)(y % 16),(int)(z % 16));
				//and re-drop ourselves!
				Vector3 lUnityPos = WorldScript.instance.mPlayerFrustrum.GetCoordsToUnity(x, y, z);
				CollectableManager.instance.QueueCollectableSpawn(
					lUnityPos,cube,lValue,mForwards,0.1f);//re-drop, but slowly
					
			}
		}
		*/
        //u wot m8
        //if (PersistentSettings.settingsIni.GetBoolean("FastConveyors",false))

        if (lValue == (ushort)eConveyorType.eMotorisedConveyor)
        {
            mrMaxPower = 125;//about 25 items
        }

        mnY = y;//just so I am sure
        CalcPenaltyFactor();

        //if we are looking at a conveyor, and the conveyor is looking at us, then rotate ourselves to face the same way as that conveyor

        //		Debug.Log("Conveyor looking along dir " + dir.ToString() + " using flags" +  flags);

        if (lValue == (ushort)eConveyorType.eBasicConveyor ||
            lValue == (ushort)eConveyorType.eConveyor ||
            lValue == (ushort)eConveyorType.eConveyorTurntable ||
            lValue == (ushort)eConveyorType.eTransportPipe)
        {

            //Ones from disk seem to break randomly. Not going to invDebug.LogWarning ("Falling at " + mVelocity.y + "/" + (WorldScript.instance.mWorldData.mrMaxFallingSpeed * Time.deltaTime).ToString ());estigate, by the time we loaded from disk, we can assume the player achieved what they wanted

            if (WorldScript.mbIsServer && !lbLoadedFromDisk)
            {
                if (!CheckAdjacentConveyor())
                {
                    mbCheckAdjacencyRotation = true;
                }
            }
        }
        CalcCarrySpeed();

        mnInstancedID = -1;//Ensure that we don't have one, and, more importantly, don't try and give one BACK!

        if (lValue == (ushort)eConveyorType.eBasicConveyor || lValue == (ushort)eConveyorType.eConveyor)
        {
            mbInstancedBase = true;
            mnInstancerType = (int)InstanceManager.eSimpleInstancerType.eT1ConveyorBase;
        }

        base.MaxNetworkSendDist = 8;//temp - probably more like 64 - base on rate?

        if (lValue == (ushort)eConveyorType.eAdvancedFilter || lValue == (ushort)eConveyorType.eConveyorFilter || lValue == (ushort)eConveyorType.eTransportPipeFilter)
        {
            base.MaxNetworkSendDist = 128;
        }

        if (IsCrafter())
        {
            base.MaxNetworkSendDist = 128;
        }
    }

	void CalcPenaltyFactor()
	{
		mnPenaltyFactor = 0;
		mbRecommendPlayerUnfreeze = false;

	    if (mValue == (ushort)eConveyorType.eConveyor || mValue == (ushort)eConveyorType.eBasicConveyor ||
	        mValue == (ushort)eConveyorType.eConveyorSlopeUp || mValue == (ushort)eConveyorType.eConveyorSlopeDown ||
	        mValue == (ushort)eConveyorType.eCorner_CW || mValue == (ushort)eConveyorType.eBasicCorner_CW ||
	        mValue == (ushort)eConveyorType.eCorner_CCW || mValue == (ushort)eConveyorType.eBasicCorner_CCW)
		{
            if (mnY-WorldScript.mDefaultOffset < BiomeLayer.CavernColdCeiling && mnY-WorldScript.mDefaultOffset > BiomeLayer.CavernColdFloor)
			{
                bool lbDoCold = true;
                if (mRoomController != null)
                {
                    if (mRoomController.mrHeatModulation >= 1.0f)
                    {
                        //hurray
                        lbDoCold = false;
                    }

                }

				//We are a T1 conveyor in an invalid location, punish the player (and encourage MM and T2s)
				mbConveyorFrozen = true;
                mbHoloDirty = true;
                mnPenaltyFactor = (int)(BiomeLayer.CavernColdCeiling - (mnY-WorldScript.mDefaultOffset));
				mnPenaltyFactor *= 100;
				if (mnPenaltyFactor > 10000) mnPenaltyFactor = 10000;
				mnCurrentPenaltyFactor = 0;
			}
			
			if (mnY-WorldScript.mDefaultOffset < BiomeLayer.CavernToxicCeiling && mnY-WorldScript.mDefaultOffset > BiomeLayer.CavernToxicFloor)
			{
                bool lbDoToxic = true;
                if (mRoomController != null)
                {
                    if (mRoomController.mrToxicRating >= 1.0f)
                    {
                        //hurray
                        lbDoToxic = false;
                    }

                }
                if (lbDoToxic)
                {
                    //We are a T1 conveyor in an invalid location, punish the player (and encourage MM and T2s)
                    mbConveyorToxic = true;


                    mnCurrentPenaltyFactor = mRand.Next(2000, 5000);

                    //Random.Range(2000,5000);
                    mnPenaltyFactor = mnCurrentPenaltyFactor - 1;//will force an update during, er, update

                    //mrCarrySpeed /= (float)Random.Range(2,50);
                }
			}
		}
		else
		{
			mnCurrentPenaltyFactor = 0;
		}
	}

	void CalcCarrySpeed()
	{

		mrCarrySpeed = TIER1_SPEED;
		
		//T2 moves quickly
		if (mValue == (ushort)eConveyorType.eTransportPipe || mValue == (ushort)eConveyorType.eTransportPipeFilter)
		{
			mrCarrySpeed = TIER2_SPEED;
		}
		//T0 does not
		if (mValue == (ushort)eConveyorType.eBasicConveyor )
		{
			mrCarrySpeed = TIER0_SPEED;
		}
        if (mValue == (ushort)eConveyorType.eBasicCorner_CW) mrCarrySpeed = TIER0_SPEED;
        if (mValue == (ushort)eConveyorType.eBasicCorner_CCW) mrCarrySpeed = TIER0_SPEED;
        if (mValue == (ushort)eConveyorType.eCorner_CW) mrCarrySpeed = TIER1_SPEED;
        if (mValue == (ushort)eConveyorType.eCorner_CCW) mrCarrySpeed = TIER1_SPEED;

        //Because I can't be bothered to deal with the compaints...
        if (mValue == (ushort)eConveyorType.eAdvancedFilter)
		{
			mrCarrySpeed = TIER2_SPEED;
		}

		if (mValue == (ushort)eConveyorType.eMotorisedConveyor)
		{
			mrCarrySpeed = TIER3_SPEED;
		}
		
		if (DifficultySettings.mbFastConveyors)//Should this affect assembly line machines?
		{
			mrCarrySpeed *= FAST_SETTING_SPEED_FACTOR;
		}

		if (DifficultySettings.mbRushMode)
		{
			mrCarrySpeed *= 2.0f;//I wonder what this is going to do...
		}

		//If conevyor is going UP, reduce speed massively
		if (mForwards.y > 0.0f)
		{
			//All tiers, initially, until I tested it.
			mrCarrySpeed *= VERTICAL_SPEED_FACTOR;
			//Debug.LogWarning(lValue.ToString() + "Conveyor heading at Y of " + mForwards.y + ": reducing Carry speed to " + mrCarrySpeed);
		}

        //Assembly line machines aren't affected
        if (DifficultySettings.mbRoboMania)
        {
            mrCarrySpeed /= 16.0f;
        }

        float lrAssemblySpeed = TIER0_SPEED;

        if (DifficultySettings.mbCasualResource) lrAssemblySpeed *= 3.0f;
		//3.06 - Assembly lines made slower. 
		if (mValue == (ushort)eConveyorType.eConveyorStamper) 		mrCarrySpeed = lrAssemblySpeed;
		if (mValue == (ushort)eConveyorType.eConveyorPCBAssembler)	mrCarrySpeed = lrAssemblySpeed;
		if (mValue == (ushort)eConveyorType.eConveyorCoiler) 		mrCarrySpeed = lrAssemblySpeed;
		if (mValue == (ushort)eConveyorType.eConveyorExtruder) 		mrCarrySpeed = lrAssemblySpeed;
		if (mValue == (ushort)eConveyorType.eConveyorPipeExtrusion) mrCarrySpeed = lrAssemblySpeed;



		mrBaseCarrySpeed = mrCarrySpeed;
	}

	void SetObjectType()
	{
		
		if (mValue == (ushort)eConveyorType.eConveyor) 				mObjectType = SpawnableObjectEnum.Conveyor;
		if (mValue == (ushort)eConveyorType.eConveyorFilter) 		mObjectType = SpawnableObjectEnum.Conveyor_Filter_Single;
		if (mValue == (ushort)eConveyorType.eTransportPipe) 		mObjectType = SpawnableObjectEnum.TransportPipe;
		if (mValue == (ushort)eConveyorType.eTransportPipeFilter) 	mObjectType = SpawnableObjectEnum.TransportPipe_Filter_Single;
        //FC2 force away from SH
        if (mValue == (ushort)eConveyorType.eConveyorStamper) 		mObjectType = SpawnableObjectEnum.Stamper_T1;
		if (mValue == (ushort)eConveyorType.eConveyorBender) 		mObjectType = SpawnableObjectEnum.AirInductor;//on purpose error
		if (mValue == (ushort)eConveyorType.eConveyorCoiler) 		mObjectType = SpawnableObjectEnum.Coiler_T1;
		if (mValue == (ushort)eConveyorType.eConveyorExtruder) 		mObjectType = SpawnableObjectEnum.Extruder_T1;
		
		if (mValue == (ushort)eConveyorType.eConveyorPipeExtrusion) mObjectType = SpawnableObjectEnum.PipeExtruder_T1;
		
		
		if (mValue == (ushort)eConveyorType.eConveyorPCBAssembler)	mObjectType = SpawnableObjectEnum.PCBAssembler_T1;
		
		if (mValue == (ushort)eConveyorType.eConveyorTurntable)		mObjectType = SpawnableObjectEnum.Turntable_T1;
		
		if (mValue == (ushort)eConveyorType.eBasicConveyor)		mObjectType = SpawnableObjectEnum.BasicConveyor;
		
		if (mValue == (ushort)eConveyorType.eAdvancedFilter)		mObjectType = SpawnableObjectEnum.Conveyor_Filter_Advanced;//FC2 force away from SH
		
		if (mValue == (ushort)eConveyorType.eConveyorSlopeUp)		mObjectType = SpawnableObjectEnum.Conveyor_SlopeUp;
		if (mValue == (ushort)eConveyorType.eConveyorSlopeDown)		mObjectType = SpawnableObjectEnum.Conveyor_SlopeDown;

        if (mValue == (ushort)eConveyorType.eMotorisedConveyor) mObjectType = SpawnableObjectEnum.Conveyor_Motorised;//FC2 force away from SH

        if (mValue == (ushort)eConveyorType.eBasicCorner_CW) mObjectType = SpawnableObjectEnum.Basic_Conveyor_Corner_CW;
        if (mValue == (ushort)eConveyorType.eBasicCorner_CCW) mObjectType = SpawnableObjectEnum.Basic_Conveyor_Corner_CCW;
        if (mValue == (ushort)eConveyorType.eCorner_CW) mObjectType = SpawnableObjectEnum.Conveyor_Corner_CW;
        if (mValue == (ushort)eConveyorType.eCorner_CCW) mObjectType = SpawnableObjectEnum.Conveyor_Corner_CCW;
    }

	// ************************************************************************************************************************************************
	public override void SpawnGameObject()
	{
		// object based on value for tiers
		SetObjectType();

		base.SpawnGameObject();
	}

	// ************************************************************************************************************************************************
	public override void DropGameObject ()
	{
		base.DropGameObject ();
		mbLinkedToGO = false;

        mbCheckedScroller = false;
        mPGScroller = null;
        mInstancedScroller = null;

        if (mnInstancedID != -1)
        {
            InstanceManager.instance.maSimpleInstancers[mnInstancerType].Remove(mnInstancedID);
            mnInstancedID = -1;
        }

	}

 	// ************************************************************************************************************************************************
	int mnLFUpdates;
	float mrReadoutTick;
	// ************************************************************************************************************************************************

	InventoryExtractionOptions mCachedHopperOptions = new InventoryExtractionOptions();

	InventoryExtractionResults mCachedHopperResults = new InventoryExtractionResults();

	void LookForHopper()
	{
		long checkX = this.mnX;
		long checkY = this.mnY;
		long checkZ = this.mnZ;
		
		int lnXMod = 0;
		int lnYMod = 0;
		int lnZMod = 0;
		
		if (mnLFUpdates % 6 == 0) lnXMod--;
		if (mnLFUpdates % 6 == 1) lnXMod++;
		if (mnLFUpdates % 6 == 2) lnYMod--;
		if (mnLFUpdates % 6 == 3) lnYMod++;
		if (mnLFUpdates % 6 == 4) lnZMod--;
		if (mnLFUpdates % 6 == 5) lnZMod++;
		
		//Never attempt to take items FROM the forwards tho (else we will take stuff out of the hopper we're putting things into)
		if (lnXMod == (int)mForwards.x &&
			lnYMod == (int)mForwards.y &&
			lnZMod == (int)mForwards.z)
		{
			return;
		}

		if (mValue == (int)eConveyorType.eMotorisedConveyor)
		{
			lnXMod = -(int)mForwards.x;
			lnYMod = -(int)mForwards.y;
			lnZMod = -(int)mForwards.z;
		}
		
		checkX += lnXMod;
		checkY += lnYMod;
		checkZ += lnZMod;
				
		Segment checkSegment = AttemptGetSegment(checkX, checkY, checkZ);
		
		if (checkSegment == null)
			return;

		var hopperEntity = checkSegment.SearchEntity(checkX, checkY, checkZ) as StorageMachineInterface;

		if (hopperEntity != null)
		{
			// Check the permissions on this inventory.
			eHopperPermissions permissions = hopperEntity.GetPermissions();

			if (permissions == eHopperPermissions.Locked)
				return;

			// Check logistics are currently allowed
			if (false == hopperEntity.InventoryExtractionPermitted)
				return;

			mCachedHopperOptions.SourceEntity = this;
			mCachedHopperOptions.RequestType = meRequestType;
			mCachedHopperOptions.ExemplarItemID = ExemplarItemID;
			mCachedHopperOptions.ExemplarBlockID = ExemplarBlockID;
			mCachedHopperOptions.ExemplarBlockValue = ExemplarBlockValue;
			mCachedHopperOptions.InvertExemplar = mbInvertExemplar;
			mCachedHopperOptions.MinimumAmount = 1;
			mCachedHopperOptions.MaximumAmount = 1;

			if (hopperEntity.TryExtract(mCachedHopperOptions, ref mCachedHopperResults))
			{
				if (mCachedHopperResults.Item != null)
				{
					AddItem(mCachedHopperResults.Item);
				}
				else
				{
					AddCube(mCachedHopperResults.Cube, mCachedHopperResults.Value, 1.0f);
				}

				mCachedHopperResults.Item = null;
                base.ImportantNetworkUpdate = true;//this will override the distance check when the next transmission goes live
                RequestImmediateNetworkUpdate(); //we collected something from a storage hopper, sync across network
			}
		}

        
	}

    // ************************************************************************************************************************************************
    public bool IsCarryingCargo()
    {
#if FC_2
        if (mCarriedCube != eCubeTypes.OreCoal)
        if (mCarriedCube != eCubeTypes.NULL) Debug.LogError("We *ARE* still using CarriedCubes! " + TerrainData.GetNameForValue(mCarriedCube, mCarriedValue));
#endif
        if (mCarriedCube != eCubeTypes.NULL) return true;
        if(mCarriedItem != null) return true;
        return false;
    }

    // ************************************************************************************************************************************************
	void OffloadCargo(long checkX, long checkY, long checkZ)
	{

        if (mCarriedCube == eCubeTypes.NULL && mCarriedItem == null)
        {
            if (WorldScript.mbIsServer)
            {
                Debug.LogError("Error, attempted to offload cargo, but both cube and carried item were null!?");
                FinaliseOffloadingCargo();
            }
            return;
        }

        if (WorldScript.mbIsServer == false)
        {

            if (IsConveyor() == false)
            {
                return;//assembly line machines DO NOT handoff on the client, only the server. This should avoid the (slightly confusing) copper bar into stamper, tin plate out...
            }
        }




		// If this is an upslope conveyor check forwards and upwards one space.
		if (mObjectType == SpawnableObjectEnum.Conveyor_SlopeUp)
		{
			checkX += (int)mUp.x;
			checkY += (int)mUp.y;
			checkZ += (int)mUp.z;
		}

		//if (mObjectType == SpawnableObjectEnum.Conveyor_SlopeDown)
		//Do nothing - the output from this is straight
		 
	//	Debug.Log("CheckX:" + checkX + ".ThisX:" + this.mnX);
	//	Debug.Log("CheckY:" + checkY + ".ThisY:" + this.mnY);
	//	Debug.Log("CheckZ:" + checkZ + ".ThisZ:" + this.mnZ);
		
	//	Debug.Log(mForwards);
		
		Segment checkSegment = AttemptGetSegment(checkX, checkY, checkZ);
		
		if (checkSegment == null)
			return;
		
		ushort lCube = checkSegment.GetCube(checkX, checkY, checkZ);

        // special case - Logistics Grommet
        if (lCube == eCubeTypes.LogisticsGrommet && !mInOffloadFromGrommet)
        {
            //If there is another PSB the OTHER side of this, hook that up
            long lDiffX = (mnX - checkX) * 2;
            long lDiffY = (mnY - checkY) * 2;
            long lDiffZ = (mnZ - checkZ) * 2;

            //todo, ensure no teleporting down stripes.

	        mInOffloadFromGrommet = true;
            OffloadCargo(mnX - lDiffX, mnY - lDiffY, mnZ - lDiffZ);
    	    mInOffloadFromGrommet = false;
            return;        
  	}

        // This flag will be set if we've decided to look for a download conveyor which should be in front and down one space.
        bool lbOnlyAllowDownSlope = false;

		if (lCube == eCubeTypes.Air)//this ensures we don't do the re-check when there's something like a hopper here. It also means no clipping through stuff!
		{
			//checkY--;
            //Assume Downward convyor matches original's orientation for those who like building pretty conveyor systems
            checkX -= (int)mUp.x;
            checkY -= (int)mUp.y;
            checkZ -= (int)mUp.z;
            checkSegment = AttemptGetSegment(checkX, checkY, checkZ);

			if (checkSegment == null) 
				return;

			lCube = checkSegment.GetCube(checkX, checkY, checkZ);
			lbOnlyAllowDownSlope = true;
		}

		bool successfullyOffloaded = false;

		// First consider conveyors separately.
		if (lCube == eCubeTypes.Conveyor)
		{
//			mbAttachedToConveyor = true;
//			mbAttachedToHopper = false;

			//this is quite a slow call. Given that we are, you know, a conveyor, and only have 1 check location, I wonder if this can be smarted up? Offload Cargo can take 100ms on huge worlds
			//We could cache the type and then only call this if it changes. Also need to check if the old one was deleted
			ConveyorEntity lConv = checkSegment.FetchEntity(eSegmentEntity.Conveyor,checkX,checkY,checkZ) as ConveyorEntity;

			// If this is a conveyor, and either we are happy for any type of conveyor, or it's a downslope
			if (lConv != null && (!lbOnlyAllowDownSlope || lConv.mObjectType == SpawnableObjectEnum.Conveyor_SlopeDown))
			{
                
				float lrDot = Vector3.Dot(mForwards,lConv.mForwards);
				//	Debug.Log("Conveyors have a dot of " + lrDot);

				bool validConveyor = true;

				if (lrDot <-0.9f)//I am not going to risk checking <1.0f
				{
					//This means we are pointing AT the conveyor - and it's pointing at us!
					validConveyor = false;
				}

				if (lConv.mObjectType == SpawnableObjectEnum.Conveyor_SlopeUp || lConv.mObjectType == SpawnableObjectEnum.Conveyor_SlopeDown)
				{
					if (lrDot < 0.9f)
					{
						//Debug.LogWarning("Can't drop off to target slope that's not in the same direction");
						validConveyor = false;
					}
				}

				//Conveyor handing to another conveyor
				//If these are assembly line machines, they shouldn't skip nor allow sideways stuff, really...
				if (validConveyor && lConv.mbReadyToConvey && lConv.mrLockTimer == 0.0f)//should this be collapsed into one ready check?
				{
					//hurrah
					if (mCarriedCube != eCubeTypes.NULL)
					{
                        ARTHERPetSurvival.instance.GotOre(mCarriedCube);

						//todo, if this is a conveyor at 90 degrees along the Y, drop in with a 50% offset
						float lrOffset = 0.5f;
						if (lConv.mForwards  == mForwards) lrOffset = 1.0f;
						//We should now pass on any 'over' simulation. T2 pipes on Fast update at 0.8/tick, so that means we're quite likely at a carrytimer of <-0.6 !
						lrOffset += mrCarryTimer;


						lConv.AddCube(mCarriedCube, mCarriedValue, lrOffset);
					}
					else
					{
						//attempt to add Item
						//Debug.Log("Conveyor has no Item to convey?");
						

						float lrOffset = 0.5f;
						if (lConv.mForwards  == mForwards) lrOffset = 1.0f;
						//We should now pass on any 'over' simulation. T2 pipes on Fast update at 0.8/tick, so that means we're quite likely at a carrytimer of <-0.6 !
						lrOffset += mrCarryTimer;
                        
                        lConv.AddItem(mCarriedItem, lrOffset);
                    }

					lConv.mItemForwards = mItemForwards;
					mCarriedCube = eCubeTypes.NULL;
					mCarriedValue = 0;
					mCarriedItem = null;
					mbStopBelt = true;
					mbConveyorBlocked = false;
					FinaliseOffloadingCargo();

					successfullyOffloaded = true;
				}
			}
		}
		else 
		{
			// If we haven't offloaded to another conveyor then attempt to offload to another entity type now.
			if (!lbOnlyAllowDownSlope)
			{

                

#if UNITY_EDITOR
                if (!CubeHelper.IsMachine(lCube))
                {
                    //If we point the conveyor at, say, a rock, this will entirely break, but only in-editor. Else we assume that it is a Mod.
                    //Debug.LogError("Correct assumption! Do not call this for non-machines");
                }
                else
                {
#endif
                    // Determine type of entity at this location.
                    eSegmentEntity entityType = EntityManager.GetEntityTypeFromBlock(lCube);



                    // Try to get the entity, it must implement the ItemConsumerInterface
                    ItemConsumerInterface targetEntity = checkSegment.FetchEntity(entityType, checkX, checkY, checkZ) as ItemConsumerInterface;




                    // We found a consumer entity
                    if (targetEntity != null)
                    {
                        // Try to deliver the item or cube.
                        if (targetEntity.TryDeliverItem(this, mCarriedItem, mCarriedCube, mCarriedValue, true))
                        {
                            // Successfully delivered the item or cube.


                            // Clear the item or cube
                            mCarriedItem = null;
                            mCarriedCube = eCubeTypes.NULL;
                            mCarriedValue = 0;

                            // Stop the belt and flag the conveyor is not blocked
                            mbStopBelt = true;
                            mbConveyorBlocked = false;

                            FinaliseOffloadingCargo();

                            // Request a network update
                            RequestImmediateNetworkUpdate();

                            successfullyOffloaded = true;
                        }
                    }
#if UNITY_EDITOR
                }
#endif
            }
        }

		// If we've still failed to offload the item then do final actions
		if (!successfullyOffloaded)
		{
			// Failed to offload this item.
			if (!mbConveyorBlocked)
			{
				// Stop the belt and mark as blocked.
				mbConveyorBlocked = true;
				mbStopBelt = true;
                base.ImportantNetworkUpdate = true;//Force a single update regardless of distance. To do : make sure this IS a single update!
                RequestImmediateNetworkUpdate();//ensure this ripples outwards to the clients
            }

			// If we are a conveyor turntable turn to face the next direction.
			if (mObjectType == SpawnableObjectEnum.Turntable_T1)
			{
				//Debug.LogWarning ("TT facing Conv, spinning" + lrDot);
				DoNextTurnTable();
				return;
			}
		}
		else
		{
			//we have successfully cleared down the cube
			meNextAnimState = eAnimState.Out;

			//Debug.LogWarning("Setting *OUT* anim!");
			mnRotationWithoutSuccess = 0;
		}

//		if (lCube == eCubeTypes.StorageHopper)
//		{
////			mbAttachedToConveyor = true;
////			mbAttachedToHopper = false;
//			
//			StorageHopper lHop = checkSegment.FetchEntity(eSegmentEntity.StorageHopper,checkX,checkY,checkZ) as StorageHopper;
//			if (lHop != null)
//			{
//				//Cube/Item path
//				if (mCarriedCube != eCubeTypes.NULL)
//				{
//					//Does the hopper have room?
//					if (lHop.mnStorageFree > 0)
//					{
//						//hurray
//						lHop.AddCube(mCarriedCube, mCarriedValue);
//
//						// Clear the item or cube
//						mCarriedCube = eCubeTypes.NULL;
//						mCarriedValue = 0;
//
//						// Stop the belt
//						mbStopBelt = true;
//
//						// Conveyor is not blocked if it successfully offloaded
//						mbConveyorBlocked = false;
//
//						RequestImmediateNetworkUpdate();
//						lHop.RequestImmediateNetworkUpdate();
//
//						FinaliseOffloadingCargo();
//					}
//					else
//					{
//						//Debug.Log("Conveyor's target hopper is full!");
//						if (!mbConveyorBlocked)
//						{
//							mbConveyorBlocked = true;
//							mbStopBelt = true;	
//						}
//
//						if (mObjectType == SpawnableObjectEnum.Turntable_T1)
//						{
//							//Debug.LogWarning ("TT facing Conv, spinning" + lrDot);
//							DoNextTurnTable();
//							return;
//						}
//					}
//				}
//				else
//				{
//					//Does the hopper have room?
//					if (lHop.mnStorageFree > 0)
//					{
//						//hurray
//						lHop.AddItem(mCarriedItem);
//
//
//						mCarriedItem = null;
//						mbStopBelt = true;
//						//Debug.Log("Conveyor offloaded Cube to a hopper");
//						mbConveyorBlocked = false;
//
//						FinaliseOffloadingCargo();
//
//						RequestImmediateNetworkUpdate();
//						lHop.RequestImmediateNetworkUpdate();
//					}
//					else
//					{
//						//Debug.Log("Conveyor's target hopper is full!");
//						if (!mbConveyorBlocked)
//						{
//							mbConveyorBlocked = true;
//							mbStopBelt = true;	
//
//							if (mObjectType == SpawnableObjectEnum.Turntable_T1)
//							{
//								DoNextTurnTable();
//							}
//						}
//					}
//				}
//			}
//			else
//			{
//				//Debug.LogWarning("Error, hopper attached to conveyor is null!");
//				//Probably a network client - this will page in, don't panic or spam!
//			}
//		}
//		else if (lCube == eCubeTypes.MassStorageInputPort)
//		{
//			//Either add our item or convert the cube to an item and then pass it along
//			//Todo, regret this later :)
//
//			MassStorageInputPort lPort = (MassStorageInputPort)checkSegment.FetchEntity(eSegmentEntity.MassStorageInputPort,checkX,checkY,checkZ);
//			if (lPort != null)
//			{
//				if (lPort.meState != MassStorageInputPort.eState.Idling) return;//input port is not ready
//				if (mCarriedItem != null)
//				{
//					if (lPort.mCarriedItem == null)
//					{
//						lPort.mCarriedItem = mCarriedItem;
//						mCarriedItem = null;
//						RemoveCube();//or item
//
//					}
//					else
//					{
//						Debug.LogWarning ("Input Port is full, cannot offload mCarriedItem");
//					}
//				}
//				if (mCarriedCube != eCubeTypes.NULL)
//				{
//					//Just noticed we don't bother carrying value
//					ItemCubeStack tempStack = ItemManager.SpawnCubeStack(mCarriedCube, TerrainData.GetDefaultValue(mCarriedCube), 1);
//					lPort.mCarriedItem = tempStack;
//					RemoveCube();//or item
//				}
//
//			}
//		}
//		else if (lCube == eCubeTypes.Macerator)
//		{
//			//Either add our item or convert the cube to an item and then pass it along
//			//Todo, regret this later :)
//			Macerator lMac = (Macerator)checkSegment.FetchEntity(eSegmentEntity.Macerator,checkX,checkY,checkZ);
//
//			if (lMac != null)
//			{
//				if (lMac.ReadyToMacerate() == false) return;
//				if (mCarriedItem != null)
//				{
//					lMac.GiveItemToMacerate(mCarriedItem);
//					mCarriedItem = null;
//					RemoveCube();//or item
//				}
//				if (mCarriedCube != eCubeTypes.NULL)
//				{
//					//Just noticed we don't bother carrying value (So 
//					ItemCubeStack tempStack = ItemManager.SpawnCubeStack(mCarriedCube, TerrainData.GetDefaultValue(mCarriedCube), 1);
//					lMac.GiveItemToMacerate(tempStack);
//					RemoveCube();//or item
//				}
//			}
//		}
//		else
//		{
//			//Debug.Log("Conveyor not looking at conveyor?");
//			/*if (lCube == eCubeTypes.NULL) Debug.Log("Conveyor looking at NULL?");
//			if (TerrainData.mEntries[lCube] != null)
//				Debug.Log("Conveyor looking at " + TerrainData.mEntries[lCube].Name);*/
////			mbAttachedToConveyor = false;
//			mbConveyorBlocked = true;
//			if (mObjectType == SpawnableObjectEnum.Turntable_T1)
//			{
//				DoNextTurnTable();
//			}
//		}

//		if (mCarriedItem == null && mCarriedCube == eCubeTypes.NULL)
//		{
//			//we have successfully cleared down the cube
//			meNextAnimState = eAnimState.Out;
//			//Debug.LogWarning("Setting *OUT* anim!");
//			mnRotationWithoutSuccess = 0;
//		}
//		else
//		{
//			//we're still carrying stuff!
//			mbStopBelt = true;
//			mbConveyorBlocked = true;
//		}
	}

	//Generic stuff to avoid having the same code EVERYWHERE
	public void FinaliseOffloadingCargo()
	{
		mbReadyToConvey = true;
		mrCarryTimer = 0.0f;
		mConveyedItems++;
		MarkDirtyDelayed();
		mnItemsThisMinute++;
        mbStopBelt = true;
        mbConveyorBlocked = false;
        mbHoloDirty = true;
        //Force an update when we hand off, approx once per 2 seconds
        //Would be nice to do this all the time for assembly machine, perhaps?
        /*if (mConveyedItems % 10 == 0)
        {
            RequestImmediateNetworkUpdate();
        }*/
        //RequestImmediateNetworkUpdate();
        //The issue here is that the client is just beginning to move a previous conveyor item, and then the server goes HEY THERES NOTHING ON HERE!
        //We would be MUCH better working in the other direction; force a sync when an item is added, not when an item is removed
        //All critical movement is now sync'd
    }
	// ************************************************************************************************************************************************
    // This is now nigh-identicall to Finalised Offloading Cargo.
	public void RemoveCube()
	{
		mbReadyToConvey = true;
		mrCarryTimer = 0.0f;
		mCarriedCube = eCubeTypes.NULL;
        mCarriedValue = 0;
        mbStopBelt = true;
		mbConveyorBlocked = false;
		mConveyedItems++;
		MarkDirtyDelayed();
        RequestImmediateNetworkUpdate();
        
	}
    //Should FAC just wipe both item and cube? <-yes it fucking should
    public void RemoveItem()
    {
        mCarriedItem = null;
        
        RequestImmediateNetworkUpdate();
    }
    //should we also reset ReadyToConvey at this point?
    public void RemoveAndClearBelt()
    {
        RemoveItem();
        RemoveCube();
        
    }

    // ************************************************************************************************************************************************
    //todo, overload this with an offset *and* rework the shitty 'rely on the carrytimer being zero' thing for a boolean flag
    //THIS ONLY EXISTS FOR MOD FUNCTIONALITY
    public void AddItem(ItemBase lItem)
    {
        AddItem(lItem, 1.0f);//100% of remaining carry distance
    }
    public void AddItem(ItemBase lItem, float lrOffset)
    {

        if (lrOffset < 0)
        {
            Debug.LogWarning("Why do we have an offset of <0?" + System.Environment.StackTrace);
            lrOffset = 0;
        }

		if (mnCarryNetworkDebt != 0)
		{
			mnCarryNetworkDebt--;
			return;
		}

		if (lItem == null)
		{
			Debug.LogWarning ("Warning, NULL item added to conveyor?!" + System.Environment.StackTrace);
			return;
		}

        if (lItem.mType == ItemType.ItemStack)
            if ((lItem as ItemStack).mnAmount == 0)
            {
                Debug.LogError("Error, attempting to add an ItemStack of ZERO to the Hopper?![" + ItemManager.GetItemName(lItem) +"]" + System.Environment.StackTrace);
                return;
            }


		if (mbReadyToConvey == false)
		{
			Debug.LogError("Conveyor was not ready to receive item!");
		}
		if (mCarriedCube != eCubeTypes.NULL) Debug.LogError("Conveyor cannot collect item whilst still carrying cube!");
		mCarriedItem = lItem;
        
        // For corners override carry timer and item forward.
        if (this.mValue == (ushort)eConveyorType.eCorner_CCW || this.mValue == (ushort)eConveyorType.eBasicCorner_CCW)
        {
            this.mrCarryTimer = CARRY_TIME * 0.5f;
            this.mrVisualCarryTimer = VISUAL_CARRY_TIME * 0.5f;
            this.mItemForwards = mRight;
        }
        else if (this.mValue == (ushort)eConveyorType.eCorner_CW || this.mValue == (ushort)eConveyorType.eBasicCorner_CW)
        {
            this.mrCarryTimer = CARRY_TIME * 0.5f;
            this.mrVisualCarryTimer = VISUAL_CARRY_TIME * 0.5f;
            this.mItemForwards = mLeft;
        }
        else
        {
            mrCarryTimer = CARRY_TIME * lrOffset;
            mrVisualCarryTimer = VISUAL_CARRY_TIME * lrOffset;
            mItemForwards = mForwards;//If this was passed in by an external conveyor, then this will get overwritten.
        }

        mbUnityCubeNeedsUpdate = true;
        mbReadyToConvey = false;
        mbItemHasBeenConverted = false;
        mbHoloDirty = true;
        MarkDirtyDelayed();
        //Debug.Log("Conveyor now starting with item " + lItem.mnItemID);
        RequestImmediateNetworkUpdate();

        #if UNITY_EDITOR
        if (lItem.mType == ItemType.ItemCubeStack)
            if (CubeHelper.IsIngottableOre((lItem as ItemCubeStack).mCubeType))
            {
                if((lItem as ItemCubeStack).mCubeValue == 0)
                {
                    Debug.LogError("Error, Conveyor got Item ore with no value?" + System.Environment.StackTrace);
                }
            }
        #endif
	}
	// ************************************************************************************************************************************************
	public void AddCube(ushort lCube, ushort lValue, float lrScalar)
	{
        if (lrScalar < 0)
        {
            Debug.LogWarning("Why do we have an offset of <0?" + System.Environment.StackTrace);
            lrScalar = 0;
        }
		if (mnCarryNetworkDebt != 0)
		{
			mnCarryNetworkDebt--;
			return;
		}
		if (!mbReadyToConvey)
		{
			Debug.LogWarning("Conveyor was not ready to receive cube!");
		}
		if (mCarriedItem != null) Debug.LogError("Conveyor cannot collect cube whilst still carrying item!");
		mCarriedCube = lCube;
		mCarriedValue = lValue;
		mrCarryTimer = CARRY_TIME * lrScalar;
		mrVisualCarryTimer = VISUAL_CARRY_TIME * lrScalar;
		mbUnityCubeNeedsUpdate = true;
		mbReadyToConvey = false;
        mbHoloDirty = true;
        MarkDirtyDelayed();

        RequestImmediateNetworkUpdate();
		
		//Debug.Log("Conveyor now starting with cube " + lCube);

        #if UNITY_EDITOR
        if (CubeHelper.IsIngottableOre(lCube))
        {
            if(lValue == 0)
            {
                Debug.LogError("Error, Conveyor got Cube ore with no value?" + System.Environment.StackTrace);
            }
        }
        #endif
		
	}
	// ************************************************************************************************************************************************
    float TurntableStuck;
	public override void LowFrequencyUpdate()
	{
     
        if (mbConveyorBlocked && LastRequestedScrollSpeed != 0)
        {
            if (LastRequestedScrollSpeed > 0 && mbStopBelt == false)//we start at -99
            {
                //PRETTY sure this is due to 'behind us' (about 99% true)
                #if UNITY_EDITOR
                Debug.LogError("Error, what circumstance has the conveyor blocked but the belt moving @ " + LastRequestedScrollSpeed + " ? Behind:" + mbConveyorIsBehindPlayer + "StopReq" + mbStopBelt);
                #endif
            }
            mbStopBelt = true;
        }   
            
        if (mbConveyorBlocked)
        {
            if (mrBlockedTime < 0.5f)
            {
                mrBlockedTime += LowFrequencyThread.mrPreviousUpdateTimeStep;

                if (mrBlockedTime > 1.0f)
                {
                    //We have been blocked for 1 second, send this out to the clients
                    RequestImmediateNetworkUpdate();//ensure this ripples outwards to the clients 
                }
            }
            else
            {
                mrBlockedTime += LowFrequencyThread.mrPreviousUpdateTimeStep;
            }

        }
        else
        {
            if (mrBlockedTime > 0)
            {
                RequestImmediateNetworkUpdate();//ensure this ripples outwards to the clients 
                mbHoloDirty = true;
            }
            mrBlockedTime = 0;
        }
        //System.Threading.Thread.Sleep(500);//Use this to provide unnecessary load
        if (PersistentSettings.mbHeadlessServer == false)
        {
            if (mSegment != null)
            {
                if (mSegment.mbNeedsUnityUpdate == false)
                {
                    mSegment.mbNeedsUnityUpdate = true;
                    Debug.LogError("Error, [" + mType.ToString() + "] correcting Bad Error of segment with no Unity updates!");
                }
            }
        }

        UpdatePlayerDistanceInfo();//ALWAYS

		mrIPMCounter-= LowFrequencyThread.mrPreviousUpdateTimeStep;
		if (mrIPMCounter < 0)
		{
			mnItemsPerMinute = mnItemsThisMinute;
			mnItemsThisMinute = 0;
			mrIPMCounter = 60;
		}
		//(now done intelligently in the UI)
	//	if (mnItemsPerMinute == 0) mnItemsPerMinute = mnItemsThisMinute;//so the first minute isn't kinda rubbish


		if (mrSleepTimer > 0)
		{
			mbStopBelt = true;
			mrSleepTimer -= LowFrequencyThread.mrPreviousUpdateTimeStep;
			//We just woke up. Spin!
			if (mrSleepTimer < 0.0f)
			{
				if (mObjectType == SpawnableObjectEnum.Turntable_T1)
				{
					//DO NOT SPINT - we span AFTER we failed for the 4th time, and are probably looking at a valid position
					//DoNextTurnTable();
				}
			}
			return;
		}

        //I've found a few ways to break these recently, let's belt-and-brace them
        if (mObjectType == SpawnableObjectEnum.Turntable_T1)
        {
            TurntableStuck+=LowFrequencyThread.mrPreviousUpdateTimeStep;
            if (TurntableStuck > 15.0f)//this is the emergency 'was built or loaded stupidly' change and shouldn't generally trigger
            {
                DoNextTurnTable();
            }
        }

        //This is slightly annoying, but there's no guarantee of load order - if we're risking an area penalty, we need to recheck
        if (mValue == (ushort)eConveyorType.eConveyor || mValue == (ushort)eConveyorType.eBasicConveyor ||
            mValue == (ushort)eConveyorType.eConveyorSlopeUp || mValue == (ushort)eConveyorType.eConveyorSlopeDown ||
            mValue == (ushort)eConveyorType.eCorner_CW || mValue == (ushort)eConveyorType.eBasicCorner_CW ||
            mValue == (ushort)eConveyorType.eCorner_CCW || mValue == (ushort)eConveyorType.eBasicCorner_CCW)
        {
            if (mnY - WorldScript.mDefaultOffset < BiomeLayer.CavernColdCeiling && mnY - WorldScript.mDefaultOffset > BiomeLayer.CavernColdFloor)
            {
                CalcPenaltyFactor();
            }
            if (mnY - WorldScript.mDefaultOffset < BiomeLayer.CavernToxicCeiling && mnY - WorldScript.mDefaultOffset > BiomeLayer.CavernToxicFloor)
            {
                CalcPenaltyFactor();
            }
        }

        mrMynockCounter -= LowFrequencyThread.mrPreviousUpdateTimeStep;
		mrMynockDebounce -= LowFrequencyThread.mrPreviousUpdateTimeStep;

		//At this point, the mynock counter is counting down, but we loaded off disk 1-5 seconds ago with a mynock on board
		if (mrMynockCounter < 0.0f)
		{
			if (WorldScript.mbIsServer)
			{
				if (mbMynockNeeded)
				{
                    Vector3 offset = Vector3.zero;
                    Vector3 look = this.mForwards;
                    if (mValue == (ushort)ConveyorEntity.eConveyorType.eConveyorSlopeDown)
                    {
                        look = Vector3.RotateTowards(this.mForwards, this.mUp, -Mathf.PI / 4, 0.0f);
                        offset = Vector3.Scale(this.mUp, new Vector3(0.95f, 0.95f, 0.95f)) + look;
                    }
                    else if (mValue == (ushort)ConveyorEntity.eConveyorType.eConveyorSlopeUp)
                    {
                        // Mynock will still face down the slope rather than along conveyor forwards because geometry is hard
                        look = Vector3.RotateTowards(-this.mForwards, this.mUp, -Mathf.PI / 4, 0.0f);
                        offset = Vector3.Scale(this.mUp, new Vector3(0.95f, 0.95f, 0.95f)) + look;
                    }

                    long lSpawnX = mnX + (int)mUp.x;
                    long lSpawnY = mnY + (int)mUp.y;
                    long lSpawnZ = mnZ + (int)mUp.z;

                    Segment lSeg = AttemptGetSegment(lSpawnX, lSpawnY, lSpawnZ);

                    MobEntity mob = null;

                    if (lSeg != null)
                    {
                        mob = MobManager.instance.SpawnMob(MobType.HiveConveyorMynock,
                                                                     lSeg,
                                                                     lSpawnX,
                                                                     lSpawnY,
                                                                     lSpawnZ,
                                                                     offset, //Vector3.forward);
                                                                     look,
                                                                     this.mFlags);//cubeData.meFlags);
                    }
					if (mob == null || lSeg == null) 
					{
						//else try again next frame I guess
					}
					else
					{
                        (mob as HiveMob).mUp = this.mUp;
                        (mob as HiveMob).mbRotDirty = true;
                        mbMynockNeeded = false;
						mrMynockCounter = 5;
					}
					

					
					
					//Debug.Log("**** Conveyor forcing Mynock onto itself ****");
				}
				else
				{
					mrMynockCounter = 0.0f;
				}
			}

		}
	     
		if (mrMynockDebounce < 0.0f)
			mrMynockDebounce = 0.0f;

		// If we have a mynock set the debounce timer to prevent more spawns for x minutes after it is killed.
		if (mrMynockCounter > 0.0f)
			mrMynockDebounce = MYNOCK_DEBOUCE_TIME;

		if (mnCurrentPenaltyFactor != mnPenaltyFactor)
		{
		//	if (mnLFUpdates % 10 == 0)
			{
				if(mnCurrentPenaltyFactor > mnPenaltyFactor) mnCurrentPenaltyFactor-=100;
				if (mnCurrentPenaltyFactor < 100) mnCurrentPenaltyFactor = 100;//this is 'none'
				if(mnCurrentPenaltyFactor < mnPenaltyFactor) mnCurrentPenaltyFactor++;

				if (mnCurrentPenaltyFactor > mnPenaltyFactor/2) mbRecommendPlayerUnfreeze = true;

				//as close to smooth as I can manage at 3hz
				mrCarrySpeed = mrBaseCarrySpeed / (mnCurrentPenaltyFactor / 100.0f);

				mbConveyorNeedsColourChange = true;

				//we could flag up for the conveyor to change colour
			}
		}

#if UNITY_EDITORx
        mbConveyorNeedsColourChange = true;
#endif

        if (mbCheckAdjacencyRotation && CheckAdjacentConveyor())
		{
			// this is only on server because the client can't have mbCheckAdjacencyRotation set to true!
			mbCheckAdjacencyRotation = false;
		}


	
		mrRobotLockTimer-=LowFrequencyThread.mrPreviousUpdateTimeStep;

		if (mrLockTimer > 0.0f)
		{

			//decrement carry timer until half way, then stop for whatever is telling us to stop
			mrLockTimer -= LowFrequencyThread.mrPreviousUpdateTimeStep;
			if (mrLockTimer < 0) mrLockTimer = 0;
			 
			if (mrCarryTimer < 0.5f) 
				mrCarryTimer += 0.1f;//move back to the correct positionb 
			else
				mrCarryTimer -= LowFrequencyThread.mrPreviousUpdateTimeStep * mrCarrySpeed; 
		}
		else
		{
			float lrCarryTimer = mrCarryTimer;
			mrCarryTimer -= LowFrequencyThread.mrPreviousUpdateTimeStep * mrCarrySpeed;
			if (lrCarryTimer >= 0.5f && mrCarryTimer <=0.5f)
			{
				//we just hit the half way point \o/

				if (mObjectType == SpawnableObjectEnum.Turntable_T1)
				{
					DoNextTurnTable();
				}

			}
		}
		if (mrCarryTimer < 0.0f) mrCarryTimer = 0.0f;

        //This ensures that headless servers act correctly, even tho they don't have any graphics. Does this ACTUALLY do anything?
        if(PersistentSettings.mbHeadlessServer)
        {
            mrVisualCarryTimer = mrCarryTimer;
        }

		//NOTE: you can't use Time.deltaTime outside the unity thread.
		// you CAN use existing GameManager.time, which unfortunately is total time, not delta.
		mnLFUpdates++;

		//this avoids us activating the in and out animation of the machine in the wrong order on the last LF tick
		mnLFCarryFrames++;

		if (mnLFCarryFrames == 1)
		{
			//only animate if the type is valid!
			if (GetItemConversionID(mCarriedItem) != -1)
		    {
				meNextAnimState = eAnimState.In;
			}

		}

		if (mbConveyorBlocked)
		{
			//85% chance of unblocking, else come back next frame, to add in randomness to fix constant choice of A, rather than A,B,C,A,B,A,C,A,C etc
			//How to do random numbers here... !
			//System.Random lRand = new System.Random()
		}

		bool lbLookForHopper = false;
	

		if (meRequestType != eHopperRequestType.eNone) lbLookForHopper = true;
		if (ExemplarItemID != -1) lbLookForHopper = true;
		if (ExemplarBlockID != 0) lbLookForHopper = true;//The value can be zero, unfortunately

		if (mbReadyToConvey == false) lbLookForHopper = false;

		if (mValue == (int)eConveyorType.eMotorisedConveyor)
		{
			if(mrCurrentPower < PowerPerItem)
			{
				lbLookForHopper = false;
			}
		}

		if (lbLookForHopper)
		{
			if (WorldScript.mbIsServer)
			{

				LookForHopper();

				if (mValue == (int)eConveyorType.eMotorisedConveyor)
				{
					if (mbReadyToConvey == false)
					{
						//we collected an item! This cost power!
						mrCurrentPower -= PowerPerItem;
					}
				}
			}
			else
			{
				//We do not do this. The server tells us this has happened.
			}
		}
		//To consider; convert the item half way along the conveyor; this means that we update the item
		if (!mbReadyToConvey && mrCarryTimer <= 0.0f)
		{
			if (mbItemHasBeenConverted == false) ConvertCargo();



            //Attempt to offload to another conveyor, or a hopper, or a grommet
            //We only need to search forwards!	
            long checkX = (this.mnX + (long)mForwards.x);
            long checkY = (this.mnY + (long)mForwards.y);
            long checkZ = (this.mnZ + (long)mForwards.z);
            OffloadCargo(checkX, checkY, checkZ);
		}
	}
	#region SINGLE_PURPOSE_MACHINE_CONVERSION
	// ************************************************************************************************************************************************
	void ConvertCargo()
	{
		mbItemHasBeenConverted = true;
		if (mCarriedCube != eCubeTypes.NULL) return;//we are not item-based cargo
		if (mCarriedItem == null) 
		{
#if UNITY_EDITOR
            if (WorldScript.mbIsServer)
            {
                Debug.LogError("Error, conveyor didn't have an item OR a cube?");
            }
#endif
            return;//probably a network sync issues
		}
		if (mCarriedItem.mType == ItemType.ItemCubeStack) return;//we don't want to deal with items of cubes either



		mCarriedItem = GetItemConversion(mCarriedItem);

        if (mCarriedItem == null || mCarriedItem.mnItemID == -1)
        {
            //client only...?
            Debug.LogError("Error, machine " + ((eConveyorType)mValue).ToString() +  " failed to convert type!");
        }
            
            

        RequestImmediateNetworkUpdate();

		

		//if on easy mode, and it's not a valid conversion, do nothing
		//Else destroy the object and spawn rubble :>
	}
	// ************************************************************************************************************************************************
	int GetPipeFromBar(ItemBase lItem)
	{
		//Debug.Log ("Getting Plate From Bar " + ItemEntry.GetNameFromID(lItem.mnItemID));
		if (lItem.mnItemID == ItemEntries.CopperBar) 	return ItemEntries.CopperPipe;
		if (lItem.mnItemID == ItemEntries.TinBar) 		return ItemEntries.TinPipe;
		if (lItem.mnItemID == ItemEntries.IronBar) 		return ItemEntries.IronPipe;
		if (lItem.mnItemID == ItemEntries.LithiumBar) 	return ItemEntries.LithiumPipe;
		if (lItem.mnItemID == ItemEntries.GoldBar) 		return ItemEntries.GoldPipe;
		if (lItem.mnItemID == ItemEntries.NickelBar) 	return ItemEntries.NickelPipe;
		if (lItem.mnItemID == ItemEntries.TitaniumBar) 	return ItemEntries.TitaniumPipe;
		
		
		return -1;//unable to convert this into a bar. If we're on a harder difficulty, spawn rubble.
	}
	// ************************************************************************************************************************************************
	int GetPlateFromBar(ItemBase lItem)
	{
		//Debug.Log ("Getting Plate From Bar " + ItemEntry.GetNameFromID(lItem.mnItemID));
		if (lItem.mnItemID == ItemEntries.CopperBar) 	return ItemEntries.CopperPlate;
		if (lItem.mnItemID == ItemEntries.TinBar) 		return ItemEntries.TinPlate;
		if (lItem.mnItemID == ItemEntries.IronBar) 		return ItemEntries.IronPlate;
		if (lItem.mnItemID == ItemEntries.LithiumBar) 	return ItemEntries.LithiumPlate;
		if (lItem.mnItemID == ItemEntries.GoldBar) 		return ItemEntries.GoldPlate;
		if (lItem.mnItemID == ItemEntries.NickelBar) 	return ItemEntries.NickelPlate;
		if (lItem.mnItemID == ItemEntries.TitaniumBar) 	return ItemEntries.TitaniumPlate;


		return -1;//unable to convert this into a bar. If we're on a harder difficulty, spawn rubble.
	}
	// ************************************************************************************************************************************************
	int GetWireFromBar(ItemBase lItem)
	{
	//	Debug.Log ("Getting Wire From Bar " + ItemEntry.GetNameFromID(lItem.mnItemID);
		if (lItem.mnItemID == ItemEntries.CopperBar) 	return ItemEntries.CopperWire;
		if (lItem.mnItemID == ItemEntries.TinBar) 		return ItemEntries.TinWire;
		if (lItem.mnItemID == ItemEntries.IronBar) 		return ItemEntries.IronWire;
		if (lItem.mnItemID == ItemEntries.LithiumBar) 	return ItemEntries.LithiumWire;
		if (lItem.mnItemID == ItemEntries.GoldBar) 		return ItemEntries.GoldWire;
		if (lItem.mnItemID == ItemEntries.NickelBar) 	return ItemEntries.NickelWire;
		if (lItem.mnItemID == ItemEntries.TitaniumBar) 	return ItemEntries.TitaniumWire;

		return -1;//unable to convert this into a bar. If we're on a harder difficulty, spawn rubble.
	}
	// ************************************************************************************************************************************************
	 int GetCoilFromWire(ItemBase lItem)
	{
	//	Debug.Log ("Getting Coil From Wire " + ItemEntry.GetNameFromID(lItem.mnItemID);

		if (lItem.mnItemID == ItemEntries.CopperWire) 	return ItemEntries.CopperCoil;
		if (lItem.mnItemID == ItemEntries.TinWire) 		return ItemEntries.TinCoil;
		if (lItem.mnItemID == ItemEntries.IronWire) 	return ItemEntries.IronCoil;
		if (lItem.mnItemID == ItemEntries.LithiumWire) 	return ItemEntries.LithiumCoil;
		if (lItem.mnItemID == ItemEntries.GoldWire) 	return ItemEntries.GoldCoil;
		if (lItem.mnItemID == ItemEntries.NickelWire) 	return ItemEntries.NickelCoil;
		if (lItem.mnItemID == ItemEntries.TitaniumWire) return ItemEntries.TitaniumCoil;


		
		return -1;//unable to convert this into a bar. If we're on a harder difficulty, spawn rubble.
	}
	// ************************************************************************************************************************************************
	int GetPCBFromCoil(ItemBase lItem)
	{
	//	Debug.Log ("Getting PCB From Coil " + ItemEntry.GetNameFromID(lItem.mnItemID);

		if (lItem.mnItemID == ItemEntries.CopperCoil) 	return ItemEntries.BasicPCB; 		
		if (lItem.mnItemID == ItemEntries.TinCoil) 		return ItemEntries.PrimaryPCB; 		 	
		if (lItem.mnItemID == ItemEntries.IronCoil) 	return ItemEntries.HardenedPCB; 		 	
		if (lItem.mnItemID == ItemEntries.LithiumCoil) 	return ItemEntries.ChargedPCB; 		 	
		if (lItem.mnItemID == ItemEntries.TitaniumCoil) return ItemEntries.LightweightPCB; 		
		if (lItem.mnItemID == ItemEntries.NickelCoil) 	return ItemEntries.FortifiedPCB; 		 	
		if (lItem.mnItemID == ItemEntries.GoldCoil) 	return ItemEntries.ConductivePCB; 		 

		return -1;//unable to convert this into a bar. If we're on a harder difficulty, spawn rubble.
	}
	// ************************************************************************************************************************************************

	int GetItemConversionID(ItemBase lItem)
	{
		if (lItem == null) return -1;
		if (lItem.mType == ItemType.ItemCubeStack) return -1;//we can''t want to deal with items of cubes

		if (mValue == (ushort)eConveyorType.eConveyorStamper)
		{
			return GetPlateFromBar(lItem);
		}

		if (mValue == (ushort)eConveyorType.eConveyorPipeExtrusion)
		{
			return GetPipeFromBar(lItem);
		}

		if (mValue == (ushort)eConveyorType.eConveyorExtruder)
		{
			return GetWireFromBar(lItem);

		}
		if (mValue == (ushort)eConveyorType.eConveyorCoiler)
		{
			return GetCoilFromWire(lItem);
		}
		if (mValue == (ushort)eConveyorType.eConveyorPCBAssembler)
		{
			return GetPCBFromCoil(lItem);
		
		}

		return -1;
	}

	// ************************************************************************************************************************************************
	ItemBase GetItemConversion(ItemBase lItem)
	{
		int lnID = GetItemConversionID(lItem);

		if (lnID == -1) return lItem;//We cannot convert this
		if (lnID == 0) 
		{
			Debug.LogWarning("Warning, converting item with ID of 0?");
			return lItem;//We cannot convert this
		}

		return ItemManager.SpawnItem(lnID); 
	}
#endregion
	// ************************************************************************************************************************************************
	int mnUpdates;

	public override void UnitySuspended ()
	{
		if (mCarriedObjectCube != null)
		{
			//below line was to fix leaks, why removed? did we change our mind on leaks?
			//GameObject.Destroy(mCarriedObjectCube.GetComponent<MeshFilter>().mesh);
			mCarriedObjectCube = null;
		}
		//we instantiate this, so we much delete it
		if (mCarriedObjectItem != null)
		{
			GameObject.Destroy(mCarriedObjectItem);
		}
		mCarriedObjectParent = null;
		mBeltObject = null;
		mConveyorObject = null;
	}
    // ************************************************************************************************************************************************

	void ToggleVisuals ()
	{
        bool lbConveyorConsideredVisible = true;

        //within 8 metres, things must be live (else we lose stuff at the edge of the screen), really horrid when spinning fast/on OVR
        if (mDotWithPlayerForwards < 0 && mDistanceToPlayer > 8) lbConveyorConsideredVisible = false;

        if (mSegment.mbOutOfView == true || mbRaycastVisible == false || !AmInSameRoom()) lbConveyorConsideredVisible = false;


        if (lbConveyorConsideredVisible == false)
		{
			if (mbConveyorIsBehindPlayer == false)
			{
				//Conveyor is NOW behind the player
				mbConveyorIsBehindPlayer = true;
                if (mnInstancedID == -1) mConveyorObject.SetActive(false);

                if (mCarriedObjectCube != null && mCarriedObjectCube.activeSelf == true) mCarriedObjectCube.SetActive(false);
                if (mCarriedObjectItem != null && mCarriedObjectItem.activeSelf == true) mCarriedObjectItem.SetActive(false);
				mCarriedObjectParent.SetActive(false);
				mBeltObject.SetActive(false);
			///	Debug.Log("CB behind player, hiding");
			}
			
		}
		else
		{
			if (mbConveyorIsBehindPlayer == true)
			{
				//Conveyor is NO LONGER behind the player
				mbConveyorIsBehindPlayer = false;
                if (mnInstancedID == -1) mConveyorObject.SetActive(true);
				if (mBeltObject != null) mBeltObject.SetActive(true);
				
				//If we have a cube, turn this on
                //Unless I remember wrongly, we NEVER carry CarriedCubes anymore - these are converted to single ItemStacks/Items
				if (mCarriedCube != eCubeTypes.NULL) 
				{
                    if (mCarriedObjectItem != null)//this means we managed to instantiate an item prefab from the cube
                    {
                        if (mCarriedObjectItem.activeSelf == false) mCarriedObjectItem.SetActive(true);
                    }
                    else
                    {
                        if (mCarriedObjectCube != null && mCarriedObjectCube.activeSelf == false) mCarriedObjectCube.SetActive(true);
                    }
				}

				if (mCarriedItem != null)
				{
                    //We can be carrying an CubeStack, but have predicated an item instead; cope with this!
                    if (mCarriedItem.mType == ItemType.ItemCubeStack)
                    {
                        if (mCarriedObjectItem != null)//this means we managed to instantiate an item prefab from the cube
                        {
                            if (mCarriedObjectItem.activeSelf == false) mCarriedObjectItem.SetActive(true);
                        }
                        else
                        {
                            if (mCarriedObjectCube != null && mCarriedObjectCube.activeSelf == false)
                            {
                                mCarriedObjectCube.SetActive(true);
                            }
                        }
                    }
                    else
                    {
                        if (mCarriedObjectItem != null && mCarriedObjectItem.activeSelf == false)
                        {
                            mCarriedObjectItem.SetActive(true);
                        }
                    }
				}
				//show parent when in front of player
				mCarriedObjectParent.SetActive(true);
			//	Debug.Log("CB now in front of player, showing");
				//Below may override this
				//The issue is probably the delayed instantiation actually
			}
		}
	}
	// ************************************************************************************************************************************************
	bool mbConveyorIsBehindPlayer;
    public float MaxDistToSeeCarryCube = 32;
	void LodAngledConveyor ()
	{
		//conveyor is at some other abitrary angle
		
		if (mCarriedCube != eCubeTypes.NULL)//We are carrying a cube
		{
			bool lbHideCarryCube = false;
			
            if (mDistanceToPlayer > MaxDistToSeeCarryCube) //todo, fiddle this # based on zoom/detail/fps?
				lbHideCarryCube = true;
			
			if (lbHideCarryCube == true && mCarriedObjectCube.activeSelf == true) mCarriedObjectCube.SetActive(false);
			if (lbHideCarryCube == false && mCarriedObjectCube.activeSelf==false) mCarriedObjectCube.SetActive(true);
			
		}
		
		if (mDistanceToPlayer > 32)
		{
			if  (mBeltObject.activeSelf) mBeltObject.SetActive(false);
		}
		else
		{
			if (!mBeltObject.activeSelf) mBeltObject.SetActive(true);
		}
		
		//Is not the same as..
		/*
		if (mDistanceToPlayer > 32)
			if  (mBeltObject.activeSelf) mBeltObject.SetActive(false);
		else
			if (!mBeltObject.activeSelf) mBeltObject.SetActive(true);
		*/
	}
	// ************************************************************************************************************************************************
	void LodUprightConveyor ()
	{
		//If we are more-or-less in line with it, clip out at about 16 metres; increase this number up to 48 metres as the player goes up
		
		bool lbConveyorActive = true;
		
		if (mVectorToPlayer.y > 4.0f) lbConveyorActive = false;//Less than 4m below it, hide the conveyor
		if (mDistanceToPlayer > 32) lbConveyorActive = false;//more than 2 segments away, hide conveyor

        if (mbConveyorIsBehindPlayer) lbConveyorActive = false;
		
		if (mBeltObject.activeSelf != lbConveyorActive) mBeltObject.SetActive(lbConveyorActive);
		
        //This conflicts with lodding elsewhere
        /*
		if (mCarriedCube != eCubeTypes.NULL)//We are carrying a cube
		{
			bool lbHideCarryCube = false;
			
            if (mbConveyorIsBehindPlayer)
            {
                lbHideCarryCube = true;
            }
            else//This is the most likely check
            {

                if (mVectorToPlayer.y > 4.0f) //Less than 2m below it, hide the cube
				lbHideCarryCube = true;
			
                if (mDistanceToPlayer > 32.0f + (CamDetail.FPS / 2.0f)) //todo, fiddle this # based on zoom/detail/fps?
				lbHideCarryCube = true;
            }
			if (lbHideCarryCube == true && mCarriedObjectCube.activeSelf == true) 
                mCarriedObjectCube.SetActive(false);
            else
			if (lbHideCarryCube == false && mCarriedObjectCube.activeSelf==false) 
                mCarriedObjectCube.SetActive(true);


            //why does this not also control the item?
			
		}*/
	}
	// ************************************************************************************************************************************************
    bool mbPrevRayCast = false;
	void ConfigLOD ()
	{
        //For the moment, I'm going to make the instancer obey the vis
        //If there are spikes, then we will obey vis *if* room ID differs *or* we're more than, say, 32m away
        if (mSegment.mbOutOfView || !AmInSameRoom())
        {
            if (mnInstancedID != -1)
            {
                InstanceManager.instance.maSimpleInstancers[mnInstancerType].Remove(mnInstancedID);
                mnInstancedID = -1;
            }
        }

        bool lbWasBehind = mbConveyorIsBehindPlayer ;
		//conveyor has a bunch of interestingly specific rendering optimisations
		//If the conveyor is upright
		//If the conveyor is more than 2m above the player, the cube isn't visible
		//If the conveyor is more than 4m above the player, the conveyor also isn't visible
		//We could technically do the same as long as the player is along the up dot, but hey
        
		//If objects are behind us, absolutely ignore them; this saves unity checking 60 times a second on the main thread
		//Debug.Log(mDotWithPlayerForwards);
		ToggleVisuals ();
		
		if (mbConveyorIsBehindPlayer == true) return;//everything is disabled, don't do per-object distnace based lodding


		
		if (mForwards.y == 0.0f)//facing up (OR ON A WALL)
		{
			
			LodUprightConveyor ();
			
		}
		else
		{
			LodAngledConveyor ();
		}
		
		//base object itself doesn't draw forever
		
	 
		bool lbDraw = true;
		if (mDistanceToPlayer > CamDetail.SegmentDrawDistance) lbDraw = false;
		if(mDistanceToPlayer > 80) lbDraw = false;//5 segments
		if (mVectorToPlayer.y > 16) lbDraw = false;
		if (mVectorToPlayer.y <-16) lbDraw = false;

        if (mnInstancedID == -1) 
        {
			if (lbDraw)
			{
				if (!mConveyorObject.activeSelf) mConveyorObject.SetActive(true);
			}
			else
			{
				if (mConveyorObject.activeSelf) mConveyorObject.SetActive(false);

			}
        }
				 
    		
	}
	
	public override void OnUpdateRotation (byte newFlags)
	{
		//Debug.Log ("Conveyor updates rotation!");

		base.OnUpdateRotation (newFlags);
		mFlags = newFlags;
		
		mForwards = SegmentCustomRenderer.GetRotationQuaternion(mFlags) * Vector3.forward;
		mForwards.Normalize();
	    mLeft = SegmentCustomRenderer.GetRotationQuaternion(mFlags) * Vector3.left;
	    mLeft.Normalize();
	    mRight = SegmentCustomRenderer.GetRotationQuaternion(mFlags) * Vector3.right;
	    mRight.Normalize();

        UpdateInstancedBase();
	}
    // ****************************************************************************************************************
    public bool mbCheckedScroller = false;
    UVScroller_PG mPGScroller;
    UVScroller_Instanced mInstancedScroller;
    public bool mbHasPGScroller;
    public bool mbHasInsScroller;
    public float CurrentScrollSpeed;
    public float LastRequestedScrollSpeed = -999;
    // 2.85ms on an 'overdone' world <-is this still valid now that we're cached? 
    void SetScrollSpeed(float lrSpeed)
    {
        if (mValue == (ushort)eConveyorType.eBasicCorner_CW) lrSpeed *= 2;
        else if (mValue == (ushort)eConveyorType.eBasicCorner_CCW) lrSpeed *= 2;
        else if (mValue == (ushort)eConveyorType.eCorner_CW) lrSpeed *= 2;
        else if (mValue == (ushort)eConveyorType.eCorner_CCW) lrSpeed *= 2;

#if UNITY_EDITOR
        if (lrSpeed != 0 && mbConveyorBlocked) Debug.LogError("Error? Told to move, but conveyor is blocked!");
#endif

        if (LastRequestedScrollSpeed == lrSpeed)
            return;//this is fine

    	LastRequestedScrollSpeed = lrSpeed;
        if (mbCheckedScroller == false)
        {
            mPGScroller = mBeltObject.GetComponent<UVScroller_PG>();
            mInstancedScroller = mBeltObject.GetComponent<UVScroller_Instanced>();
            mbCheckedScroller = true;
            if (mPGScroller         != null) mbHasPGScroller = true;
            if (mInstancedScroller  != null) mbHasInsScroller = true;
        }

        //if this is called too often, it's going to hurt :/ (It hurt, now cached)
        //If so, store a 'has' bool and interrogate and set that just once.
       
       // if (mPGScroller != null)//Half a ms for 300 items?
        if (mbHasPGScroller)
        {
            CurrentScrollSpeed = lrSpeed;
            mPGScroller.scrollSpeed = lrSpeed;
            return;
        }

        //if (mInstancedScroller != null)
        if (mbHasInsScroller)
        {
            CurrentScrollSpeed = lrSpeed;
            mInstancedScroller.scrollSpeed = lrSpeed;
            return;
        }

        CurrentScrollSpeed = -999;//what
    }
    // ****************************************************************************************************************
	public override void UnityUpdate()
	{
        #if PROFILE_CONVEYORS
        Profiler.BeginSample("ConveyorUpdate");
        #endif
		if (!mbLinkedToGO)
		{
			if (mWrapper == null || mWrapper.mbHasGameObject == false) 
			{
                #if PROFILE_CONVEYORS
                Profiler.EndSample();
                #endif
				return;
			}
			else
			{
				if (mWrapper.mGameObjectList == null) Debug.LogError("Conveyor missing game object #0?");
				if (mWrapper.mGameObjectList[0].gameObject == null) Debug.LogError("Conveyor missing game object #0 (GO)?");

				//Searching on 300 conveyors at once cost 7.37 ms. ow.
				//This is the object we move, which has the Item and the Cube childed.
				mCarriedObjectParent = mWrapper.mGameObjectList[0].gameObject.transform.Search("Moving Item").gameObject;
				mCarriedObjectCube = mWrapper.mGameObjectList[0].gameObject.transform.Search("Moving Item Obj").gameObject;
				mCarriedObjectParent.SetActive(false);

				mAnimation = mWrapper.mGameObjectList[0].GetComponent<Animation>();//In Children is expensive!

				if (mValue == (int)eConveyorType.eMotorisedConveyor)
				{
					mMotorLight = mWrapper.mGameObjectList[0].gameObject.transform.Search("ConveyorLight").GetComponent<Light>();
				}



#if UNITY_EDITOR
				//Sanity check
				if (mAnimation == null)
				{

					if (mValue == (ushort)eConveyorType.eConveyorStamper) 		Debug.LogError("Error, " + ((SpawnableObjectEnum)mObjectType).ToString() + " missing expected animation!");
					//if (mValue == (ushort)eConveyorType.eConveyorBender) 		Debug.LogError("Error, " + ((SpawnableObjectEnum)mObjectType).ToString() + " missing expected animation!");
					if (mValue == (ushort)eConveyorType.eConveyorCoiler) 		Debug.LogError("Error, " + ((SpawnableObjectEnum)mObjectType).ToString() + " missing expected animation!");
					if (mValue == (ushort)eConveyorType.eConveyorExtruder) 		Debug.LogError("Error, " + ((SpawnableObjectEnum)mObjectType).ToString() + " missing expected animation!");
					
					if (mValue == (ushort)eConveyorType.eConveyorPCBAssembler)	Debug.LogError("Error, " + ((SpawnableObjectEnum)mObjectType).ToString() + " missing expected animation!");

					
				}

#endif

				//The bit that holds the belt
				mConveyorObject = mWrapper.mGameObjectList[0].gameObject.transform.Search("ConveyorObject").gameObject;
                mConveyorObject.SetActive(false);//if this is a default conveyor, then this will be disabled when we get an instanced object anyways
                //The belt
                mBeltObject = mWrapper.mGameObjectList[0].gameObject.transform.Search("ConveyorBelt").gameObject;
                mBeltObject.SetActive(false);


                SetScrollSpeed(0);
				if (mbConveyorFrozen)
				{
					Color lCol = new Color(1.0f - (mnPenaltyFactor/10.0f),1.0f - (mnPenaltyFactor/20.0f),1.0f);
					Color lSpecularCol = new Color(1.0f - (mnPenaltyFactor/10.0f),1.0f - (mnPenaltyFactor/40.0f),1.0f);

					mBeltObject.GetComponent<Renderer>().material = PrefabHolder.instance.FrozenConveyorBelt;

					MeshRenderer[] rs = mWrapper.mGameObjectList[0].gameObject.GetComponentsInChildren<MeshRenderer>();
					foreach(MeshRenderer r in rs)
					{
						r.material.color = lCol;
						r.material.SetColor("_SpecColor", lSpecularCol);
					}


				}

				if (mbConveyorToxic)
				{
					MeshRenderer[] rs = mWrapper.mGameObjectList[0].gameObject.GetComponentsInChildren<MeshRenderer>();
					foreach(MeshRenderer r in rs)
					{
						r.material.color = Color.green;
					}
				}




				mbLinkedToGO = true;

				if (IngotObject == null)
				{
					IngotObject = GameObject.Find("Generic Ingot");
					if (IngotObject == null) Debug.LogError("Error, cannot find [Generic Ingot] for Conveyor!");
				}

				if (mObjectType == SpawnableObjectEnum.Turntable_T1)
				{
					SpecificDetail = mWrapper.mGameObjectList[0].gameObject.transform.Search("Guide").gameObject;
                    mDetailForward = SpecificDetail.transform.forward;
                    if (mDetailForward.x == float.NaN || mDetailForward.sqrMagnitude > 100 || mDetailForward.sqrMagnitude < 0.1f)
                    {
                        Debug.LogError("Error, having detected Guide bad normal of " + mDetailForward.ToString());
                        mDetailForward = SegmentCustomRenderer.GetRotationQuaternion(mFlags) * Vector3.forward;
                        mDetailForward.Normalize();
                    }
					
				}

                if (mbInstancedBase)
                {
                    if (mnInstancedID == -1)
                    {
                        
                    }
                    else
                    {
                        Debug.LogWarning("Conveyor re-linking to Instanced Base, but we already had an ID!");
                    }
                }
				
				mnUpdates = Random.Range(0,60);//ensure staggered updates
				
				//at this point we should correctly configure the carried cube
				mbJustLoaded = true; //This informs lower code to spawn the cube in the correct place and fashion
				mbUnityCubeNeedsUpdate = true;

                ConfigLOD();


            }
		}

        //this is a hack! Sometimes network conveyors are carrying nothing, but are not 'ready to convey'. As P24 Final is looming, just hack around this.
        if (WorldScript.mbIsServer == false)
        {
            if (IsCarryingCargo() == false)
            {
                mbReadyToConvey = true;
            }
        }

        if (mbConveyorNeedsColourChange)
		{
            if (mbConveyorFrozen)
            {
                mbConveyorNeedsColourChange = false;
                Color lCol = new Color(1.0f - (mnCurrentPenaltyFactor / 1000.0f), 1.0f - (mnCurrentPenaltyFactor / 2000.0f), 1.0f);
                Color lSpecularCol = new Color(1.0f - (mnCurrentPenaltyFactor / 1000.0f), 1.0f - (mnCurrentPenaltyFactor / 4000.0f), 1.0f);


                MeshRenderer[] rs = mWrapper.mGameObjectList[0].gameObject.GetComponentsInChildren<MeshRenderer>();
                foreach (MeshRenderer r in rs)
                {
                    r.material.color = lCol;
                    r.material.SetColor("_SpecColor", lSpecularCol);

                }
            }
            else//color code based on blocked
            {
#if UNITY_EDITORx
                mbConveyorNeedsColourChange = false;


                byte R = 0;
                byte G = 0;
                byte B = 0;

                if (mbConveyorBlocked) R = 32;
                if (IsCarryingCargo()) G = 32;
                if (mbReadyToConvey) B = 32;
                if (LastRequestedScrollSpeed > 0) B += 32;

                Color lCol = new Color32(R, G, B, 255);

             

                MeshRenderer[] rs = mWrapper.mGameObjectList[0].gameObject.GetComponentsInChildren<MeshRenderer>();
                foreach (MeshRenderer r in rs)
                {
                    r.material.color = lCol;
                    r.material.SetColor("_SpecColor", lCol);

                }

#endif
            }
        }


        if (mbInstancedBase && mnInstancedID == -1 && PersistentSettings.mbHeadlessServer == false)
        {
            if (mSegment.mbOutOfView == false && AmInSameRoom())
            {
                mnInstancedID = InstanceManager.instance.maSimpleInstancers[mnInstancerType].TryAdd();
                if (mnInstancedID != -1)
                {
                    UpdateInstancedBase();
                    mConveyorObject.SetActive(false);//once we have an instanced base, we can disable the old base. TODO, remove the old base entirely
                }
            }
        }
         
		/*
        if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.RightShift))
        {
            //switch to instanced
            mWrapper.mGameObjectList[0].transform.Search("ConveyorObject").GetComponent<Renderer>().material = SegmentMeshCreator.instance.ConveyorBaseInstanced;
        }
        if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.RightControl))
        {
            //switch to non-instanced
            mWrapper.mGameObjectList[0].transform.Search("ConveyorObject").GetComponent<Renderer>().material = SegmentMeshCreator.instance.ConveyorBaseDefault;
        }*/
		
		if (mbInPlayerRange == false) return;//bit of a weird decision? this might leave objects on...?

		//until the game changes, players can easily put down thousands of conveyors
		//60 leads to a 'dithering' effect as objects are paged in. 4hz should be enough tho.
		//flat rate is dumb; do it based on distance to player. Warning - still framerate dependent
		//(conveyors closer than 15 are slower, ones further, faster)

		//todo - if the player is facing away, and we're 'active', switch faster
		//overload all the LOD stuff with

        int lnPlayerDist = (int)(mDistanceToPlayer/2)+1;
		if (lnPlayerDist > 60) lnPlayerDist = 60;
        //if the conveyor is not currently visible, then do this twice as often; popup for stuff APPEARING is bad, pop... down for stuff DISAPPEARING fine, if a *tad* worse on performance
        if (mbConveyorIsBehindPlayer == true) lnPlayerDist /= 2;
        if (lnPlayerDist <=0) lnPlayerDist = 1;//every frame when up close
        //if (mbPrevRayCast != mbRaycastVisible) lnPlayerDist = 1;//either the renderer turned on, or turned off - immediately update!
        if (mnUpdates % lnPlayerDist == 0 || mbPrevRayCast != mbRaycastVisible || mSegment.OutOfViewUpdates > 30 || mbJustLoaded == true)//segment has been out of view for 30 unity frames - it's likely we'll want to shunt off ASAP
		{
            #if PROFILE_CONVEYORS
            Profiler.BeginSample("ConfigLOD");
            #endif
			ConfigLOD ();
            #if PROFILE_CONVEYORS
            Profiler.EndSample();
            #endif
		}	
		
		
		//[23/03/2014 13:56:25] Korenn: commit: Added UpdatePlayerDistanceInfo() function to MachineEntity. after calling, the variables are ONLY valid if mbInPlayerRange is true.
//[23/03/2014 13:56:43] Korenn: in any machine where you want to use that info, call it in LowFrequencyUpdate.
		
		//Object has been removed/given away, stop the belt
		if (mCarriedCube == eCubeTypes.NULL && mCarriedItem == null)
		{
			if (mCarriedObjectParent.activeSelf == true)
			{
				mCarriedObjectParent.SetActive(false);
				
			}
		
		}
        //stop belt if requested, not only if we're not carrying something!
        if (mbStopBelt)
        {
            SetScrollSpeed(0);
            mbStopBelt = false;
        }

        //Changed to make the profiler happier
        //Todo - examine why this happens so much. Do we even need to do this if we're offscreen?
        //It would be better to simply mark the cube as dirty and *IF* we are ever being rendered, do the work
        //So; todo, mark cube as dirty upon changed type, just loaded, or visual carry initiated
        //Only actually mark off *if* we actually end up ever viewing this. It's rather likely we won't, given how many conveyors carry cubes and are NEVER seen!
        //if (mrVisualCarryTimer == VISUAL_CARRY_TIME) 
        if (mbUnityCubeNeedsUpdate)
		{
            #if PROFILE_CONVEYORS
            Profiler.BeginSample("UpdateUnityCube");
            #endif
			//"A cube has just been pushed onto our conveyor"
			//Or an item, of course!
			InitiateNewCube ();
			mbUnityCubeNeedsUpdate = false;
			//This path also triggers on initial unity spawn
			if (mCarriedItem != null || mCarriedCube != eCubeTypes.NULL)
			{
                if (mbConveyorBlocked == false)
                {                    
                    float lrSpeed = mrCarrySpeed / 2.0f;
                    if (mValue == (ushort)eConveyorType.eBasicConveyor)
                        lrSpeed *= 2.0f;//this is because of the squished UVs

                    SetScrollSpeed(lrSpeed);
                }
                else
                {
                    SetScrollSpeed(0);
                }
			}
            #if PROFILE_CONVEYORS
            Profiler.EndSample();
            #endif
		}
        #if PROFILE_CONVEYORS
        Profiler.BeginSample("InstiantiationAndInitiation");
        #endif
		if (mbJustLoaded)
		{
			//"Linked to unity side game object"
			InitiateNewCube ();
		}

		if (mbRequiresCarryInstantiation)
		{
			if (mSegment.mbOutOfView == false)
			{
                if (mDistanceToPlayer < MaxDistToSeeCarryCube)
                {
    				if (mbConveyorIsBehindPlayer == false)
    				{
    					InstantiateCarryItem();
    				}
                }
			}
		}
        #if PROFILE_CONVEYORS
        Profiler.EndSample();
        #endif


		//carrying something, and the visual carry timer is still high (lock timer can visually reverse the cube to the centre to look nice)
		if (mrVisualCarryTimer > 0.0f || mrLockTimer != 0.0f)	
		{
			if (mCarriedItem != null || mCarriedCube != eCubeTypes.NULL)
			{
                #if PROFILE_CONVEYORS
                Profiler.BeginSample("UpdateVisualCarry");
                #endif
				UpdateVisualCarry (false);
                #if PROFILE_CONVEYORS
                Profiler.EndSample();
                #endif
			}
		}
		
		//Conveyor can get blocked at the moment of handoff
        if (mbConveyorBlocked)
        {
            if (mbConveyorVisuallyBlocked == false)
            {
                mbConveyorVisuallyBlocked = true;
                //just switched to Blocked, we should consider forcing the forwards for items, or at least trying

                UpdateObjectForwards(1.0f / Time.deltaTime);
            }
            if (mbStopBelt) //Under waht circumstances are we blocked, but do not need to stop the belt?
            {
                SetScrollSpeed(0);
                mbStopBelt = false;
            }
            if (LastRequestedScrollSpeed != 0)
            {
                Debug.LogError("Error, belt is blocked, no StopBelt issued, but current speed is " + LastRequestedScrollSpeed);
            }
            if (mCarriedItem == null && mCarriedCube == eCubeTypes.NULL)
            {
                Debug.LogError("Bad state - conveyor is Blocked but has no object - why?");//network client, it seems
                mbConveyorBlocked = false;//This might be the cause of all the issues regarding dangling animated belts.
                ClearConveyor();
            }
            else
            {
                //The rules on 'is it visible' are very complex if nto behind the player, and are dealt with inside LodUprightConveyor

				/*
                //We have an item, if it's not visible, this is Naughty
                if (mDistanceToPlayer < 64 && mbWellBehindPlayer == false && mbConveyorIsBehindPlayer == false)
                {
                    bool lbItemVisible = true;
                    if (mCarriedObjectParent.activeSelf == false)
                        lbItemVisible = false;

                    if (mCarriedObjectCube == null && mCarriedObjectItem == null)
                    {
                        lbItemVisible = false;//both are null, item isn't visible
                    }
                    else//ONE of them is non-null - is it visible?
                    {
                        lbItemVisible = false;
                        if (mCarriedObjectCube != null && mCarriedObjectCube.activeSelf == true) lbItemVisible = true;
                        if (mCarriedObjectItem != null && mCarriedObjectItem.activeSelf == true) lbItemVisible = true;
                    }

                        
                    
                    if (lbItemVisible == false)
                    //if (mCarriedObjectParent.activeSelf == false && mCarriedObjectCube.activeSelf == false && mCarriedObjectItem.activeSelf == false)
                    {
                        Debug.LogError("Error, conveyor is close to player, Blocked and doesn't have active CarryParent?" + mbConveyorIsBehindPlayer + " | " + mbConveyorVisuallyBlocked + "|" + mbRaycastVisible + "|" + mbConveyorIsBehindPlayer);

                        if (mCarriedObjectCube != null && mCarriedObjectCube.activeSelf == false)
                            Debug.LogError("CUBE ERROR");
                        if (mCarriedObjectItem != null && mCarriedObjectItem.activeSelf == false) 
                            Debug.LogError("ITEM ERROR");
                        
                        ConfigLOD();
                    }
                }*/
            }

        }
        else
        {
            mbConveyorVisuallyBlocked = false;
        }



		if (mObjectType == SpawnableObjectEnum.Turntable_T1)
		{
            //Looks like we can get NaN here?

            if (mForwards.x == float.NaN || mForwards.sqrMagnitude > 100 || mForwards.sqrMagnitude < 0.1f)
            {
                Debug.LogError("Error, having to renormalise Forwards of " + mDetailForward.ToString());
                mForwards = SegmentCustomRenderer.GetRotationQuaternion(mFlags) * Vector3.forward;
                mForwards.Normalize();
            }
            if (mDetailForward.x == float.NaN || mDetailForward.sqrMagnitude > 100 || mDetailForward.sqrMagnitude < 0.01f)
            {
                if (WorldScript.mbIsServer) Debug.LogError("Error, having to renormalise DetailForwards of " + mDetailForward.ToString() + " with sqrmag of " + mDetailForward.sqrMagnitude);
                mDetailForward = SegmentCustomRenderer.GetRotationQuaternion(mFlags) * Vector3.forward;

                mDetailForward.Normalize();
            }
			if (mDetailForward != mForwards)//we probably just rotated on the unity thread
			{
                //I think this breaks HORRIBLY if our FPS is <15 (as we're stepping by more than 1)
                float lrLastFrameFPS = 1.0f / Time.deltaTime;
                if (lrLastFrameFPS > 15)
                {
                    mDetailForward += (mForwards - mDetailForward) * Time.deltaTime * 15.0f;
                }
                else
                {
                    mDetailForward = mForwards;
                }

                if (mDetailForward.x == float.NaN || mDetailForward.sqrMagnitude > 100 || mDetailForward.sqrMagnitude < 0.01f)//was 0.1, but tbh that's ok - -0.2,0,0.1 is apparently < 0.1 sqrmag
                {
                    if (WorldScript.mbIsServer)
                    {
                        //This seems to be 100% related to Grommets, but I don't know how
#if UNITY_EDITOR
                        Debug.LogError("Error, ended up with DF of " + mDetailForward + " due to update with FW of " + mForwards + " at FPS " + CamDetail.FPS + " / Timestep " + (1.0f / Time.deltaTime).ToString());
#endif
                        mDetailForward = mForwards;
                    }
                }

				SpecificDetail.transform.forward = mDetailForward;
			}
		}
		
		mnUpdates++;

		//not all objects are animated controlled; those that are, are identical
		
		if (meNextAnimState != meAnimState)
		{
            meAnimState = meNextAnimState;

            if (mAnimation != null)
            {
			//	Debug.Log("Machine flipping to anim " + meNextAnimState + " on frame "+ mnUpdates);
			

				//This speed matches the carrytimer
				mAnimation[meAnimState.ToString()].speed = 0.15f;//for now

				mAnimation.CrossFade(meAnimState.ToString(),0.01f);//tostring :(
            }
            if (meAnimState == eAnimState.In)
            {
                if (mValue == (ushort)eConveyorType.eConveyorStamper)       AudioSoundEffectManager.instance.PlayPositionEffect(AudioSoundEffectManager.instance.Stamper,mWrapper.mGameObjectList[0].transform.position,1.0f,4.0f);
                if (mValue == (ushort)eConveyorType.eConveyorCoiler)        AudioSoundEffectManager.instance.PlayPositionEffect(AudioSoundEffectManager.instance.Coiler,mWrapper.mGameObjectList[0].transform.position,1.0f,4.0f);
                if (mValue == (ushort)eConveyorType.eConveyorExtruder)      AudioSoundEffectManager.instance.PlayPositionEffect(AudioSoundEffectManager.instance.Extruder,mWrapper.mGameObjectList[0].transform.position,1.0f,4.0f);
//                if (mValue == (ushort)eConveyorType.eConveyorPipeExtrusion) mObjectType = SpawnableObjectEnum.PipeExtruder_T1;
                if (mValue == (ushort)eConveyorType.eConveyorPCBAssembler)  AudioSoundEffectManager.instance.PlayPositionEffect(AudioSoundEffectManager.instance.PCBAssembler,mWrapper.mGameObjectList[0].transform.position,1.0f,4.0f);
            }

		}

        #if PROFILE_CONVEYORS
       Profiler.EndSample();
        #endif
	}

	//this can get lost

	int mnLFCarryFrames;
	enum eAnimState
	{
		eAnimUnknown,
		In,		//Play on loading a cube
		Out,		//Play at half way? or offloading?
		Idle,		//Play else
	};
	eAnimState meAnimState;
	eAnimState meNextAnimState;

	bool mbRequiresCarryInstantiation;
    // ************************************************************************************************************************
    void SanitiseItem(GameObject lObj)
    {
        TorchLightEntity lEnt = lObj.GetComponentInChildren<TorchLightEntity>();
        if (lEnt != null) GameObject.Destroy(lEnt);
        AutoUnityInstancer lAI = lObj.GetComponentInChildren<AutoUnityInstancer>();
        if (lAI != null) GameObject.Destroy(lAI);
    }
    // ************************************************************************************************************************
    void InstantiateCarryItem()
	{
		//this means we never looked at the conveyor for the entire life of the previous added item - WOOP PERFORMANCE
		if(mCarriedCube == eCubeTypes.NULL && mCarriedItem == null)
		{
			mbRequiresCarryInstantiation = false;
			return;
		}
		if (mCarriedCube != eCubeTypes.NULL)
		{
            if (mCarriedObjectItem != null)
            {
                GameObject.Destroy(mCarriedObjectItem);
                mCarriedObjectItem = null;
            }
            //Is this a cube that has an overridden, hardcoded object? If so - we'll instantiate that instead.
            SpawnableObjectEnum lType = SpawnableObjectManagerScript.instance.GetSpawnableType(mCarriedCube, 0, mCarriedValue);
            if (lType == SpawnableObjectEnum.UnknownItem)
            {

                //We are about to set the UV; this creates a new mesh; this ensures that the mesh is deleted, prior to the creation of the new one
                //GameObject.Destroy(mCarriedObjectCube.GetComponent<MeshFilter>().mesh);
                ushort displayValue = mCarriedValue;
                if (CubeHelper.IsOre(mCarriedCube))
                    displayValue = ushort.MaxValue;

                SetUVOnCubeToTerrainIndex.SetMaterialUV(mCarriedObjectCube.GetComponent<Renderer>(), mCarriedCube, displayValue, true);
                mCarriedObjectCube.SetActive(true);
                mCarriedObjectParent.SetActive(true);
                //SetUVOnCubeToTerrainIndex.SetUVOnly(mCarriedCube,10000,mCarriedObjectCube);//make items look different, using staging
                if (mCarriedObjectItem != null) mCarriedObjectItem.SetActive(false);//Previously instantiated COI, hide it
            }
            else
            {
                //setup a carried object item
                GameObject lPrefab = SpawnableObjectManagerScript.instance.maSpawnableObjects[(int)lType];
                if (lPrefab == null)
                {
                    Debug.LogError("Can't spawn a " + lType);
                }
                else
                {
                    mCarriedObjectItem = (GameObject)GameObject.Instantiate(lPrefab, mCarriedObjectParent.transform.position, mCarriedObjectParent.transform.rotation);
                    //We'll need to consider running through through a sanitising pass
                    SanitiseItem(mCarriedObjectItem);
                    mCarriedObjectItem.transform.parent = mCarriedObjectParent.transform;
                    mCarriedObjectItem.transform.localPosition = mUp * 1.5f;
                    mCarriedObjectItem.SetActive(true);
                    mCarriedObjectCube.SetActive(false);
                    mCarriedObjectParent.SetActive(true);
                }
            }

        }
		else
		{
            if (mCarriedObjectCube.activeSelf)  mCarriedObjectCube.SetActive(false);
            if (mCarriedObjectItem != null)
			{
                GameObject.Destroy(mCarriedObjectItem);
				mCarriedObjectItem = null;
			}
			//attempt to spawn/obtain Item object
			if (mCarriedObjectItem == null)
			{
				//get prefab
				
				//					Debug.LogWarning(ItemEntry.mEntries[mCarriedItem.mnItemID].Object.ToString());
				if (mCarriedItem == null) 
				{
					//This all called when mbRequiresCarryInstantiation is high; did we potentially put this high at the last frame before dropping off the item?
					Debug.LogWarning("No Carried Item, why are we trying to initiate a new cube? " + mrCarryTimer);//carrytimer is always 0 for this message
					mbRequiresCarryInstantiation = false;
					return;
				}
				
				if (mCarriedItem.mType == ItemType.ItemCubeStack)
				{

                    SpawnableObjectEnum lType = SpawnableObjectManagerScript.instance.GetSpawnableType((mCarriedItem as ItemCubeStack).mCubeType, 0, (mCarriedItem as ItemCubeStack).mCubeValue);
                    if (lType == SpawnableObjectEnum.UnknownItem)
                    {
                        ItemCubeStack stack = mCarriedItem as ItemCubeStack;
                        //we need to spawn a cube now, and possibly, later, assign it's UVs
                        SetUVOnCubeToTerrainIndex.SetMaterialUV(mCarriedObjectCube.GetComponent<Renderer>(), stack.mCubeType, stack.mCubeValue, true);
                        mCarriedObjectCube.SetActive(true);
                        mCarriedObjectParent.SetActive(true);
                        if (mCarriedObjectItem != null) mCarriedObjectItem.SetActive(false);//Previously instantiated COI, hide it
                    }
                    else
                    {
                        //setup a carried object item
                        GameObject lPrefab = SpawnableObjectManagerScript.instance.maSpawnableObjects[(int)lType];
                        if (lPrefab == null)
                        {
                            Debug.LogError("Can't spawn a " + lType);
                        }
                        else
                        {
                            mCarriedObjectItem = (GameObject)GameObject.Instantiate(lPrefab, mCarriedObjectParent.transform.position, mCarriedObjectParent.transform.rotation);
                            //We'll need to consider running through through a sanitising pass
                            SanitiseItem(mCarriedObjectItem);
                            mCarriedObjectItem.transform.parent = mCarriedObjectParent.transform;
                            mCarriedObjectItem.transform.localPosition = mUp * 1.5f;
                            mCarriedObjectItem.SetActive(true);
                            mCarriedObjectCube.SetActive(false);
                            mCarriedObjectParent.SetActive(true);
                        }
                    }

				}
				else
				{
					if (ItemEntry.mEntries[mCarriedItem.mnItemID].Object == SpawnableObjectEnum.UnknownItem)
					{
						//sensible, bespoke default
						mCarriedObjectItem = (GameObject)GameObject.Instantiate(IngotObject,mCarriedObjectParent.transform.position,mCarriedObjectParent.transform.rotation);
					}
					else
					{
						if (mCarriedItem == null) {Debug.LogError("Error, carried item was null somehow!");return;}//nulled on another thread? 
						if (ItemEntry.mEntries[mCarriedItem.mnItemID] == null) 
						{
							Debug.LogError("Error, no entry for item ID" + mCarriedItem.mnItemID);
							return;
						}//nulled on another thread? 

						int lnObjectIndex = (int)ItemEntry.mEntries[mCarriedItem.mnItemID].Object;
						GameObject lPrefab = SpawnableObjectManagerScript.instance.maSpawnableObjects[lnObjectIndex];
						
						if (lPrefab == null)
						{
							Debug.LogError("Error, couldn't find a prefab for item " + ItemEntry.GetNameFromID(mCarriedItem.mnItemID) +" with ID " + mCarriedItem.mnItemID);
						}
						
						mCarriedObjectItem = (GameObject)GameObject.Instantiate(lPrefab,mCarriedObjectParent.transform.position,mCarriedObjectParent.transform.rotation);
                        SanitiseItem(mCarriedObjectItem);
 

                    }
					mCarriedObjectItem.transform.parent = mCarriedObjectParent.transform;
					
					mCarriedObjectItem.SetActive(true);
					mCarriedObjectCube.SetActive(false);
					mCarriedObjectParent.SetActive(true);
				}
				
				
				//	Debug.LogWarning("Spawning Item!");
			}
			
		}
		mbRequiresCarryInstantiation = false;
		UpdateVisualCarry(true);//ensure cube is at correct position
	}
 
	// ************************************************************************************************************************************************
	// All this does is flag up that we'd like to instantiate at the appropriate time
	void InitiateNewCube ()
	{
		if (mCarriedCube == eCubeTypes.NULL && mCarriedItem == null) return;

		mbJustLoaded = false;
		mbRequiresCarryInstantiation = true;

		mnLFCarryFrames = 0;


	}

	void DoNextTurnTable()
	{
		Rotate(true);
		mnNumRotations++;
		if (mnNumRotations >=4) mnNumRotations = 0;
		mnRotationWithoutSuccess++;
		if (mnRotationWithoutSuccess == 5)
		{
			mrSleepTimer = 1.0f;//massively reduced, this isn't causing any load of any note, and it's annoying as hell when it gets stuck 
			mnRotationWithoutSuccess = 0;
			//Debug.LogWarning ("TT blocked, sleeping for 5 seconds");

		}
        TurntableStuck = 0.0f;
		RequestImmediateNetworkUpdate();
	}
	// ************************************************************************************************************************************************
	void Rotate(bool CW)
	{

		// fetch current flags
		byte rotateFlags = mFlags;
		
		if ((rotateFlags & CubeHelper.FACEMASK) == 0) // check for bad flags
		{
			// old shitty generation. compensate
			rotateFlags = (byte)(rotateFlags | CubeHelper.TOP);
		}
		
		rotateFlags = CubeHelper.RotateFlags(rotateFlags, CW); // clockwise for ']'

		WorldScript.instance.BuildRotateFromEntity(
			mSegment,
			mnX,
			mnY,
			mnZ,
			mCube,
			mValue,
			rotateFlags);
	}

	// ************************************************************************************************************************************************
	void UpdateVisualCarry (bool lbForcePosition)
	{

		if (mCarriedCube == eCubeTypes.NULL && mCarriedItem == null) return;

		if (mrLockTimer > 0.0f)
		{
			if (mrVisualCarryTimer < 0.5f) 
				mrVisualCarryTimer += Time.deltaTime;
			else
				mrVisualCarryTimer-=Time.deltaTime * mrCarrySpeed;
		}
		else
		{
			mrVisualCarryTimer-=Time.deltaTime * mrCarrySpeed;
		}

		if (mrVisualCarryTimer <0.0f) mrVisualCarryTimer = 0.0f;
		
		if (mrVisualCarryTimer > 0.0f || lbForcePosition)
		{
			if (mCarriedObjectParent.activeSelf == true)
			{
				//only update the transform when necessary, as it's annoyingly expensive
                if (mDistanceToPlayer < MaxDistToSeeCarryCube && mbConveyorIsBehindPlayer == false)//this check is probably pointless, as it's 
				{
					//Reduce the rate this updates, based on distance
					int lnRate = (int)(mDistanceToPlayer / 2) - 8;//increased from 5 to 8 cuz it was looking a bit ass
					if (lnRate <1) lnRate = 1;
					
					if (mnUpdates % lnRate == 0 || lbForcePosition)
					{

						Vector3 lNewPos  = new Vector3(0,0.10025f,0.5f - mrVisualCarryTimer);//this is a little expensive, so don't do it if not necessary

						if (mObjectType == SpawnableObjectEnum.Conveyor_SlopeUp)
						{
							lNewPos.y += (1.0f - mrVisualCarryTimer);//check I dont need to add This.mUp * mrVisualCarryTimer
						}
						if (mObjectType == SpawnableObjectEnum.Conveyor_SlopeDown)
						{
							lNewPos.y += (mrVisualCarryTimer);//check I dont need to add This.mUp * mrVisualCarryTimer
						}

						mCarriedObjectParent.transform.localPosition = lNewPos;

						mCarriedObjectParent.transform.forward = mItemForwards;

						//uh, we could lerp the position so there's no jumping at all, and copy the position fropm the previous conveyor too.
					}


					//if (mCarriedObjectItem != null) Debug.LogWarning("Visual Update Item!");
				}

			}
			else
			{
				//mCarriedObjectParent.SetActive(true);//why were we doing this? Ifit's behind the player, don't acvt/deactivate it constantly
			}
		}

		//if I feel like polishing more, the modification should be different along different axes; Y axis faster than XZ interpolation

        //This is pretty expensive; 1.5 ms for 500 items
        //I suspect a count down and 'just set it to the forwards' would be an idea on handoff/setting of the ItemForwards?
        //For now, only do visible ones
        if (mbConveyorIsBehindPlayer == false && mDistanceToPlayer < 64)
        {
            // Turn speed needs to be much slower on a basic or the animation looks artificial.
            if (mValue == (ushort)eConveyorType.eBasicCorner_CW || mValue== (ushort)eConveyorType.eBasicCorner_CCW ||mValue == (ushort)eConveyorType.eBasicConveyor)
                UpdateObjectForwards(0.8f);
    	    else
            	UpdateObjectForwards(5.0f);
        }

        //Note, this doesn't work... when? when conveyor blocked?
	}
	void UpdateObjectForwards(float lrScalar)
	{
			if (mObjectType == SpawnableObjectEnum.Conveyor_SlopeUp)
    			mItemForwards += ((mForwards + mUp) - mItemForwards) * Time.deltaTime * lrScalar;//this is within the Unity update
    		else if (mObjectType == SpawnableObjectEnum.Conveyor_SlopeDown)
    			mItemForwards += ((mForwards - mUp) - mItemForwards) * Time.deltaTime * lrScalar;//this is within the Unity update
    		else
    			mItemForwards += (mForwards - mItemForwards) * Time.deltaTime * lrScalar;//this is within the Unity update

	}
	// ************************************************************************************************************************************************
	public override bool ShouldSave ()
	{
		return true;
	}
	// ************************************************************************************************************************************************
	public override void Write (System.IO.BinaryWriter writer)
	{
		//if (mValue == 1) Debug.LogWarning("Conveyor Entity Saving request type" + meRequestType.ToString());
		//Save Item
		ItemFile.SerialiseItem(mCarriedItem,writer);
		//Save Cube
		writer.Write(mCarriedCube);
		writer.Write(mrCarryTimer);			//FloatToByte for network version
		writer.Write(mrVisualCarryTimer);	//FloatToByte for network version
		writer.Write((int) meRequestType);
		
		writer.Write(mConveyedItems);

		writer.Write(mCarriedValue);
		writer.Write((ushort)0); // 2 bytes padding
		float lrDummy = 0;

		writer.Write(mrCarrySpeed);//mostly for networking	//FloatToByte for network version
		writer.Write(mnCurrentPenaltyFactor);				//Byte for network

		writer.Write(mnPenaltyFactor);						//Byte for network
		writer.Write(ExemplarItemID);
		writer.Write(ExemplarBlockID);		//2 bytes
		writer.Write(ExemplarBlockValue);	//2 bytes
		//writer.Write(lrDummy);
		writer.Write(mbInvertExemplar);
		bool lbDummy = false;
		writer.Write((byte)mrMynockCounter);//we really only care about 0 and a-bit-above-zero
		writer.Write(lbDummy);
		writer.Write(lbDummy);
		
	}
	// ************************************************************************************************************************************************
	bool mbJustLoaded;
	public int mnCarryNetworkDebt;//how many items to immediately trash to keep up
	public override void Read (System.IO.BinaryReader reader, int entityVersion)
	{
		mbJustLoaded = true;
		//Load Item
		mCarriedItem = ItemFile.DeserialiseItem(reader);
		//Load Cube
		mCarriedCube = reader.ReadUInt16();
		mrCarryTimer = reader.ReadSingle();
		mrVisualCarryTimer = reader.ReadSingle();
		meRequestType = (eHopperRequestType)reader.ReadInt32();

		int lnOldCarried = (int)mConveyedItems;
	
		mConveyedItems = reader.ReadUInt32();
        //This cannot happen, right? This is only called for servers, yes? (yes, this is the Read function, so does nothing)
        //If we are a client, and the number of items the server thinks we've carried differ, get it back into sync, because... why?
		if (WorldScript.mbIsServer == false && false)            //We're a client
		{
			if (mbConveyorBlocked)                      //We're blocked
			{
				if (lnOldCarried != 0)                  //We have transported at least one item
				{
					mnCarryNetworkDebt += (int)mConveyedItems - lnOldCarried;   //How many more items the server has carried than we have
					if (mnCarryNetworkDebt < 0) mnCarryNetworkDebt = 0;         //Can't store negative
					if (mnCarryNetworkDebt >= 1)
					{
						ClearConveyor();
						mnCarryNetworkDebt--;
					}
				}
			}
		}
		
		if (mCarriedCube != eCubeTypes.NULL || mCarriedItem != null)
		{
			mbReadyToConvey = false;
		}
		
		mCarriedValue = reader.ReadUInt16();
		reader.ReadUInt16(); // dummy padding
		

		mrCarrySpeed = reader.ReadSingle();
		mnCurrentPenaltyFactor = reader.ReadInt32();
		mnPenaltyFactor = reader.ReadInt32();
		ExemplarItemID = reader.ReadInt32();


		ExemplarBlockID = reader.ReadUInt16();
		ExemplarBlockValue = reader.ReadUInt16();

		mbInvertExemplar = reader.ReadBoolean();

		byte lMynock = reader.ReadByte();
		mrMynockCounter = (float)lMynock;
		if (mrMynockCounter <0) mrMynockCounter = 0;;//not possible
		if (mrMynockCounter > 5) mrMynockCounter = 0;//not possible, hence corruption, hence get stuffed
		//temp!
		//mrMynockCounter = 5;//temp temp temp temp
		//Do this for crazy mynocks all the time :-)
		if (mrMynockCounter != 0) 
		{
			mbMynockNeeded = true;
			//Debug.Log("Conveyor will request Mynock within " + mrMynockCounter + "s");//rly? as a client? should this be true?
		}

		bool lbDummy = reader.ReadBoolean();
		lbDummy = reader.ReadBoolean();

		//reader.ReadSingle(); // dummy

		if (mnPenaltyFactor == 0)
		{
			CalcPenaltyFactor();//it's probably ok, but it's also very possibly old, and only newly-built cold conveyors will react
		}

		if (mrCarrySpeed == 0.0f)
		{
			CalcCarrySpeed();//old data, we can't have a speed of 0 
		}

		//I was an idiot, and lots of the ACFs have this saved. 0 is Air, or something. -1 is the NO EXEMPLAR
		//Default values for the win
		if (ExemplarItemID == 0)
		{
			ExemplarItemID = -1;
		}

		//Now set the string
		if (ExemplarItemID != -1)
		{
			ItemBase lTemp = ItemManager.SpawnItem(ExemplarItemID); 
			ExemplarString = ItemManager.GetItemName(lTemp);

		}
		if (ExemplarBlockID != 0)
		{
			ItemBase lTemp = ItemManager.SpawnCubeStack(ExemplarBlockID, ExemplarBlockValue,1);
			ExemplarString = ItemManager.GetItemName(lTemp);
		}

		// If there is an exemplar set on an advanced filter then the RequestType should be set to Any, but it looks like sometimes it is set to None.
		// Storage updates now mean it is important that it is correct.
		if ((ExemplarItemID >= 0 || ExemplarBlockID > 0) && meRequestType == eHopperRequestType.eNone)
		{
			meRequestType = eHopperRequestType.eAny;
		}

        #if UNITY_EDITOR
        if (CubeHelper.IsIngottableOre(mCarriedCube))
        {
            if (mCarriedValue == 0)
            {
                Debug.LogError("Error, we got ore with no value?");
            }
        }
        #endif


	}

    // ************************************************************************************************************************************************
    public override void WriteNetworkUpdate(System.IO.BinaryWriter writer)
    {
        //If Conveyor == Empty, Write True, Exit Early.
        //if (mValue == 1) Debug.LogWarning("Conveyor Entity Saving request type" + meRequestType.ToString());
        //The thought occurs that empty conveyors probably don't actually request a transmission, but if we do have that happen, LOTS of things can be skipped here.
        //Save Item
        ItemFile.SerialiseItem(mCarriedItem, writer);
        //Save Cube
        writer.Write(mCarriedCube);
        writer.Write(mCarriedValue);
        if (mrCarryTimer < 0)
        {
            Debug.LogWarning("Error, writing network update and CarryTimer was " + mrCarryTimer);
            mrCarryTimer = 0;
        }
        if (mrVisualCarryTimer < 0)
        {
            Debug.LogWarning("Error, writing network update and mrVisualCarryTimer was " + mrVisualCarryTimer);
            mrVisualCarryTimer = 0;
        }
        //why signed byte?
        writer.Write(NetworkServerIO.FloatToSByte(mrCarryTimer));         //FloatToByte for network version
        writer.Write(NetworkServerIO.FloatToSByte(mrVisualCarryTimer));   //FloatToByte for network version (should we even be transmitting this?)
        //ISN'T THIS JUST ZERO FOR PERSISTENT SERVER???
        writer.Write((byte)meRequestType);

        writer.Write(mConveyedItems);

        //  writer.Write(mrCarrySpeed);//mostly for networking	    //FloatToByte for network version (ugh, no, it's in a rubbish 0, 0.15, 4 range)
      //  writer.Write((ushort)mnCurrentPenaltyFactor);               //2 Bytes for network (can get as high as 10,000 only)

      //  writer.Write(mnPenaltyFactor);                              //Byte for network (no, can get as high as 10,000)
      //This is calculated and simulated by the client, and pretty much the entire gameplay is an irrelevancy

        if (ExemplarItemID <= 0 && ExemplarBlockID <= 0)
        {
            writer.Write(false);//no exemplar, transmit the minimum
        }
        else
        {
            //This could be optimsed - we can't have an item AND a block, but exemplars are relatively rare
            writer.Write(true);
            writer.Write(ExemplarItemID);
            writer.Write(ExemplarBlockID);      //2 bytes
            writer.Write(ExemplarBlockValue);   //2 bytes
            writer.Write(mbInvertExemplar);
        }
        
                                            
      
        writer.Write((byte)mrMynockCounter);//we really only care about 0 and a-bit-above-zero
      

    }
    // ************************************************************************************************************************************************
    public override void ReadNetworkUpdate(System.IO.BinaryReader reader)
    {
        mbJustLoaded = true;
        //Load Item
        mCarriedItem = ItemFile.DeserialiseItem(reader);                        //If we don't have an item only then should we read the Cube types
        //Load Cube
        mCarriedCube = reader.ReadUInt16();
        mCarriedValue = reader.ReadUInt16();                //Move this up by the CarriedCube write 

        mrCarryTimer = NetworkServerIO.SByteToFloat(reader.ReadSByte());
        mrVisualCarryTimer = NetworkServerIO.SByteToFloat(reader.ReadSByte());
        meRequestType = (eHopperRequestType)reader.ReadByte();                 //byte!

        int lnOldCarried = (int)mConveyedItems;

        mConveyedItems = reader.ReadUInt32();

        //If we are a client, and the number of items the server thinks we've carried is incorrect, fix this now
        if (WorldScript.mbIsServer == false && false)
        {
            if (mbConveyorBlocked)
            {
                if (lnOldCarried != 0)
                {
                    mnCarryNetworkDebt += (int)mConveyedItems - lnOldCarried;   //How many more items the server has carried than we have
                    if (mnCarryNetworkDebt < 0) mnCarryNetworkDebt = 0;
                    if (mnCarryNetworkDebt >= 1)
                    {
                        ClearConveyor();
                        mnCarryNetworkDebt--;
                    }
                }
            }
        }

        if (mCarriedCube != eCubeTypes.NULL || mCarriedItem != null)
        {
            mbReadyToConvey = false;
        }

    

        CalcCarrySpeed();                                   //I doubt this needs to be called every time we receive a network call
        //Realistcally, no-one does this, and no-one can reset it apart from the server, and all round the idea was a good one, but didn't work.
        
        //these are very big!
     //   mnCurrentPenaltyFactor = (int)reader.ReadUInt16();  //We shouldn't send this, but simulate it locally instead (we won't be THAT far off the server!)
    //    mnPenaltyFactor = reader.ReadInt32();               //Max penalty factor is 10,000, so this can be put into a int 16

        bool lbHasExemplar = reader.ReadBoolean();

        if (lbHasExemplar)
        {
            //  mrCarrySpeed = reader.ReadSingle();
       
            ExemplarItemID = reader.ReadInt32();            //This can probably be u16 (65k max item ID)
            ExemplarBlockID = reader.ReadUInt16();
            ExemplarBlockValue = reader.ReadUInt16();
            mbInvertExemplar = reader.ReadBoolean();        //Should this be inside HasExemplar, saving another byte?
        }

     

        

        byte lMynock = reader.ReadByte();
        mrMynockCounter = (float)lMynock;
        if (mrMynockCounter < 0) mrMynockCounter = 0; ;//not possible
        if (mrMynockCounter > 5) mrMynockCounter = 0;//not possible, hence corruption, hence get stuffed
                                                     //temp!
                                                     //mrMynockCounter = 5;//temp temp temp temp
                                                     //Do this for crazy mynocks all the time :-)
        if (mrMynockCounter != 0)
        {
            mbMynockNeeded = true;
            //Debug.Log("Conveyor will request Mynock within " + mrMynockCounter + "s");//rly? as a client? should this be true?
        }
 
        //Any reason not just to calculate this instead of reading it?
        if (mnPenaltyFactor == 0)
        {
            CalcPenaltyFactor();//it's probably ok, but it's also very possibly old, and only newly-built cold conveyors will react
        }

        if (mrCarrySpeed == 0.0f)
        {
            CalcCarrySpeed();//old data, we can't have a speed of 0 
        }

        //I was an idiot, and lots of the ACFs have this saved. 0 is Air, or something. -1 is the NO EXEMPLAR
        //Default values for the win
        if (ExemplarItemID == 0)
        {
            ExemplarItemID = -1;
        }

        //Should this happen only if the player looks at us? We're potentially storing upwards of 100k copies of these strings, and clearing and setting them *constantly*
        
        if (ExemplarItemID != -1)
        {
            ItemBase lTemp = ItemManager.SpawnItem(ExemplarItemID);
            ExemplarString = ItemManager.GetItemName(lTemp);

        }
        if (ExemplarBlockID != 0)
        {
            ItemBase lTemp = ItemManager.SpawnCubeStack(ExemplarBlockID, ExemplarBlockValue, 1);
            ExemplarString = ItemManager.GetItemName(lTemp);
        }

        // If there is an exemplar set on an advanced filter then the RequestType should be set to Any, but it looks like sometimes it is set to None.
        // Storage updates now mean it is important that it is correct.
        if ((ExemplarItemID >= 0 || ExemplarBlockID > 0) && meRequestType == eHopperRequestType.eNone)
        {
            meRequestType = eHopperRequestType.eAny;
        }

#if UNITY_EDITOR
        if (CubeHelper.IsIngottableOre(mCarriedCube))
        {
            if (mCarriedValue == 0)
            {
                Debug.LogError("Error, we got ore with no value?");
            }
        }
#endif


    }

    // ******************* Network Syncing *************************
    //We should probably only network update if we've changed since the previous update; gained, lost or moved an object.
    public override bool ShouldNetworkUpdate ()
	{
		return true;
	}
	
	// use defaults for WriteNetworkUpdate and ReadNetworkUpdate
	
	// ************************************************************************************************************************************************
	public override void OnDelete()
	{
        if (mnInstancedID != -1)
        {
            InstanceManager.instance.maSimpleInstancers[mnInstancerType].Remove(mnInstancedID);
            mnInstancedID = -1;
        }

        base.OnDelete();

        if (WorldScript.mbIsServer == false) return;
	//	Debug.LogWarning("Conveyor OnDelete");
		//Drop Item if we have it (TODO ONCE WE HAVE ITEMS!)
		//Drop Cube if we have it!
		if (mCarriedCube != eCubeTypes.NULL)
		{
			ItemManager.DropNewCubeStack(mCarriedCube, mCarriedValue,1, mnX, mnY, mnZ, Vector3.zero);
			                             //TerrainData.mEntries[mCarriedCube].DefaultValue, 
			
//			Vector3 lUnityPos = WorldScript.instance.mPlayerFrustrum.GetCoordsToUnity(this.mnX, this.mnY, this.mnZ);
//			
//			//Right now we do not carry any items that care about their Value (machines, etc)
//				CollectableManager.instance.QueueCollectableSpawn(
//					lUnityPos,mCarriedCube,0,mForwards,0.1f);//re-drop, but slowly
//			Debug.Log ("Dropping " + mCarriedCube + " at " + lUnityPos.ToString());
		}
		if (mCarriedItem != null)
		{
			//CollectableManager.instance.QueueCollectableSpawn(
			ItemManager.instance.DropItem(mCarriedItem, mnX,mnY,mnZ, Vector3.zero);
		}
		
     
	}


 	//This code rotates a conveyor to rotates to match with a conveyor facing exactly in the opposite direction.
	//It'll technically trigger on load and correct any stupid-ass mistakes, but once it's in circulation, should fix a LOT of the annoying conveyor build times.
	private bool CheckAdjacentConveyor ()
	{
 
		//Debug.LogWarning("Conveyor looking along vector " + forward);

		Vector3 forward = CubeHelper.GetDirectionVector((byte)(mFlags & CubeHelper.FACEMASK));
		forward = SegmentCustomRenderer.GetRotationQuaternion(mFlags) * Vector3.forward;



		long x = (long)Mathf.Round(forward.x);
		long y = (long)Mathf.Round(forward.y);
		long z = (long)Mathf.Round(forward.z);

		long checkX = this.mnX + x;
		long checkY = this.mnY + y;
		long checkZ = this.mnZ + z;
		
		Segment checkSegment = AttemptGetSegment(checkX, checkY, checkZ);
		
		if (checkSegment == null)
		{
		//	Debug.LogWarning("Warning, Conveyor cannot rotate as targetsegment is null");
		//	Debug.LogWarning("Conveyor looking along x: " + x + ".y: " + y + ".z:" + z);
			Debug.Log("Missing segment - edge of loaded segment?");
			return false;//can't do the check, the segment we want to check into is null. This will never happen for players. (How can they build facing into a frustrum that isn't loaded!)
		}
		
		ushort lCube = checkSegment.GetCube(checkX, checkY, checkZ);

		if (lCube == eCubeTypes.NULL)
		{
			Debug.Log("Missing block - edge of loaded area?");
			return false;//not relevant, player does not build in null segments
		}

		if (lCube == eCubeTypes.Conveyor)
		{

			ConveyorEntity lConv = checkSegment.FetchEntity(eSegmentEntity.Conveyor,checkX,checkY,checkZ) as ConveyorEntity;
			if (lConv == null)
			{
				Debug.Log("Missing conveyor - was it made in the last frame?");
			}
			else
			{
				//Get the cached conveyor forwards now - does this share our forwards?

				//Debug.Log(mForwards.ToString("F2") + " V " + lConv.mForwards.ToString("F2")); 

				float lrDot = Vector3.Dot(mForwards,lConv.mForwards);
			//	Debug.Log("Conveyors have a dot of " + lrDot);

				if (lrDot <-0.9f)//I am not going to risk checking <1.0f
				{
                    if (mObjectType == SpawnableObjectEnum.Turntable_T1)
                    {
                        DoNextTurnTable();
                    }
                    else
                    {
                        //DO NOT ROTATE IF WE ARE FACING *TOWARDS* A TURNTABLE

                        if (lConv.mObjectType == SpawnableObjectEnum.Turntable_T1) 
                        {
                        }
                        else
                        {
        					//alter our forwards to match; rotate ourselves accordingly
        					//Debug.Log ("Build rotate order to match adjacent conveyor");

                            if (lrDot == -1)
                            {
                                // MadVandal: If 180 degrees then just do a double rotation which avoid distorting a belt mounted against a perpendicular surface...not sure WHY that
                                // happens but this avoids it.
                                byte newFlags = CubeHelper.RotateFlags(this.mFlags, true);
                                newFlags = CubeHelper.RotateFlags(newFlags, true);
                                WorldScript.instance.BuildRotateFromEntity(mSegment, mnX, mnY, mnZ, mCube, mValue, newFlags);
                            }
                            else WorldScript.instance.BuildRotateFromEntity(mSegment, mnX, mnY, mnZ, mCube, mValue, lConv.mFlags);
                        }
                    }
					
					// set in world, but without a build command because that would break shit
//					mSegment.SetFlagsNoChecking((int)(mnX % 16), (int)(mnY % 16), (int)(mnZ % 16), mFlags);
//					mSegment.RequestSave();//unsure if this should be delayed; ore extractor code did direct save

//					mForwards = SegmentCustomRenderer.GetRotationQuaternion(mFlags) * Vector3.forward;
//					mForwards.Normalize();
					
					// and rotate our machine (if we already have a game object)
					//I believe this cannot happen, as we are only doing this at constructor time.
					/*
					if (mWrapper != null && mWrapper.mbHasGameObject)
					{
						// we can't actually rotate the machine, we'll have to do that via checking on the unity thread :(
						mTargetRotation = SegmentCustomRenderer.GetRotationQuaternion(newFlags);
						mbRotateModel = true;
					}
*/
				}
			}

		}
		// return true - check completed. if it wasn't a conveyor that's fine too.
		return true;
	}	

	// update the flags on the machine (and rotate machine!) so it faces towards the given direction
	void RotateConveyorTo (Vector3 direction)
	{
		byte newFlags = CubeHelper.TOP;
		
		/// up / down are a special case:
		if (direction.y > 0.5f) // up
		{
			// pick a random side and orientation that will make it point upwards
			newFlags = (byte)(CubeHelper.NORTH | (2 << CubeHelper.ORIENTATION_SHIFT)); // I think 2 is up?
			
		}
		else if (direction.y < -0.5f) // down
		{
			// ditto downwards
			newFlags = (byte)(CubeHelper.NORTH | (0 << CubeHelper.ORIENTATION_SHIFT)); // I think 0 is down
		}
		else
		{
			// face is top, orientation decides which direction it points
			
			// orientation 0 is south?
			int orientation = 0;
			
			if (direction.x < -0.5f) // west
				orientation = 1;
			else if (direction.z < -0.5f) // north
				orientation = 2;
			if (direction.x > 0.5f) // east
				orientation = 3;
			
			newFlags = (byte)(CubeHelper.TOP | (orientation << CubeHelper.ORIENTATION_SHIFT));
		}
		
		if (newFlags != mFlags)
		{
			// now apply the flags
			mFlags = newFlags;
			
			// set in world, but without a build command because that would break shit
			mSegment.SetFlagsNoChecking((int)(mnX % 16), (int)(mnY % 16), (int)(mnZ % 16), mFlags);
            mSegment.RequestDelayedSave();
			
			// and rotate our machine (if we already have a game object)
			if (mWrapper != null && mWrapper.mbHasGameObject)
			{
				// we can't actually rotate the machine, we'll have to do that via checking on the unity thread :(
				mTargetRotation = SegmentCustomRenderer.GetRotationQuaternion(newFlags);
				mbRotateModel = true;
			}

            UpdateInstancedBase();

			mForwards = SegmentCustomRenderer.GetRotationQuaternion(mFlags) * Vector3.forward;
			mForwards.Normalize();
		    mLeft = SegmentCustomRenderer.GetRotationQuaternion(this.mFlags) * Vector3.left;
		    mLeft.Normalize();
		    mRight = SegmentCustomRenderer.GetRotationQuaternion(this.mFlags) * Vector3.right;
		    mRight.Normalize();

            //Assuming the player is using the (R)otate function, the Up Vector cannot change
            //	mUp = SegmentCustomRenderer.GetRotationQuaternion(mFlags) * Vector3.up;
            //mUp.Normalize();
        }
    }

    //Not a machine. Not any sort of filter.
    public bool IsGenericConveyor()
    {
        if (mValue == (ushort)eConveyorType.eConveyor) return true;
        if (mValue == (ushort)eConveyorType.eBasicConveyor) return true;
        if (mValue == (ushort)eConveyorType.eTransportPipe) return true;
        if (mValue == (ushort)eConveyorType.eMotorisedConveyor) return true;//leave this in until people complain
        if (mValue == (ushort)eConveyorType.eConveyorSlopeDown) return true;
        if (mValue == (ushort)eConveyorType.eConveyorSlopeUp) return true;

        return false;//assembly line machines

    }

    //Not a machine. Belt, Pipe or Filter only, no assembly line machines. 
    public bool IsConveyor()
    {
        if (mValue == (ushort)eConveyorType.eConveyor) return true;
        if (mValue == (ushort)eConveyorType.eBasicConveyor) return true;
        if (mValue == (ushort)eConveyorType.eTransportPipe) return true;
        if (mValue == (ushort)eConveyorType.eTransportPipeFilter) return true;
        if (mValue == (ushort)eConveyorType.eAdvancedFilter) return true;
        if (mValue == (ushort)eConveyorType.eMotorisedConveyor) return true;
        if (mValue == (ushort)eConveyorType.eConveyorFilter) return true;
        if (mValue == (ushort)eConveyorType.eConveyorSlopeDown) return true;
        if (mValue == (ushort)eConveyorType.eConveyorSlopeUp) return true;
        if (mValue == (ushort)eConveyorType.eConveyorTurntable) return true;

        if (mValue == (ushort)eConveyorType.eBasicCorner_CCW) return true;
        if (mValue == (ushort)eConveyorType.eBasicCorner_CW) return true;

        if (mValue == (ushort)eConveyorType.eCorner_CCW) return true;
        if (mValue == (ushort)eConveyorType.eCorner_CW) return true;

        return false;//assembly line machines

    }

    //Not a machine. Belt, Pipe or Filter only, no assembly line machines. 
    public bool IsCrafter()
    {
        if (mValue == (ushort)eConveyorType.eConveyorStamper) 		return true;
		if (mValue == (ushort)eConveyorType.eConveyorPCBAssembler)	return true;
		if (mValue == (ushort)eConveyorType.eConveyorCoiler) 		return true;
		if (mValue == (ushort)eConveyorType.eConveyorExtruder) 		return true;
		if (mValue == (ushort)eConveyorType.eConveyorPipeExtrusion) return true;

        return false;//not assembly line machines

    }

    //******************** PowerConsumerInterface **********************
    public float GetRemainingPowerCapacity()
	{
		
		return mrMaxPower - mrCurrentPower;
	}
	
	public float GetMaximumDeliveryRate()
	{
		
		return 500;
	}
	
	public float GetMaxPower()
	{
		
		return mrMaxPower;
	}
	
	public bool DeliverPower(float amount)
	{
		if (amount > GetRemainingPowerCapacity()) amount = GetRemainingPowerCapacity();
		
		mrCurrentPower += amount;
		MarkDirtyDelayed();
		return true;
	}
	
	
	public bool WantsPowerFromEntity(SegmentEntity entity)
	{
		if (mValue == (int)eConveyorType.eMotorisedConveyor) return true;
		return false;
	}	

	/****************************************************************************************/

	/// <summary>
	/// Called when the holobase has been opened and it requires this entity to add its
	/// visualisations. If there is no visualisation for an entity return null.
	/// 
	/// To receive updates each frame set the <see cref="HoloMachineEntity.RequiresUpdates"/> flag.
	/// </summary>
	/// <returns>The holobase entity visualisation.</returns>
	/// <param name="holobase">Holobase.</param>
	public override HoloMachineEntity CreateHolobaseEntity(Holobase holobase)
	{
		var creationParameters = new HolobaseEntityCreationParameters(this);
		creationParameters.RequiresUpdates = true;

		var primaryVisualisation = creationParameters.AddVisualisation(holobase.mPreviewCube);

		if (mValue == (ushort)ConveyorEntity.eConveyorType.eTransportPipe) 
		{
			primaryVisualisation.Prefab = holobase.Pipe;
		}
		else if (mValue == (ushort)ConveyorEntity.eConveyorType.eConveyorSlopeUp) 
		{
			primaryVisualisation.Prefab = holobase.mPreviewWedge;
			primaryVisualisation.PositionAdjustment = new Vector3(0f, -0.125f + 0.5f, 0f);
		}
		else if (mValue == (ushort)ConveyorEntity.eConveyorType.eConveyorSlopeDown) 
		{
			primaryVisualisation.Prefab = holobase.mPreviewWedge;
			primaryVisualisation.RotationAdjustmentY = 180;
			primaryVisualisation.PositionAdjustment = new Vector3(0f, -0.125f + 0.5f, 0f);
		}
		else
		{
			primaryVisualisation.Scale = new Vector3(1f, 0.2f, 1f);
			primaryVisualisation.PositionAdjustment = new Vector3(0f, -0.125f, 0f);
		}

		// TODO: Do I need to be able to override rotation, this was previously using Quaternion.identity
		var conveyorCube = creationParameters.AddVisualisation("Conveyor Cube", holobase.m5SidedPreviewCube);
		conveyorCube.PositionAdjustment = primaryVisualisation.PositionAdjustment + new Vector3(0,0.225f,0);
		conveyorCube.Scale = new Vector3(.25f, .25f, .25f);
		conveyorCube.Color = Color.white;

        mbHoloDirty = true;

        return holobase.CreateHolobaseEntity(creationParameters);

		//lEntityToAdd = AddEntityToList(lEntity);
		//This is done on spawn, as pipes need a different scale
		//	lEntityToAdd.mVisualisationObject.transform.localScale = new Vector3(1.0f, 0.2f, 1.0f);
		//	Debug.Log("Holobase found Conveyor");

		//LaserPowerTransmitter lLaser = (LaserPowerTransmitter)lMachine.mEntity;

		//Conveyor cube
		//				lEntityToAdd.mSecondaryObject = (GameObject)Instantiate(m5SidedPreviewCube, lEntityToAdd.mVisualisationObject.transform.position + new Vector3(0,0.225f,0), Quaternion.identity);
		//				//le fun :-) (will this even work?!?)
		//				lEntityToAdd.mSecondaryObject.transform.localScale = new Vector3(.25f, .25f, .25f);
		//				lEntityToAdd.mSecondaryObject.SetActive(true);
		//				lEntityToAdd.mSecondaryObject.name = "Holo Conveyor Cube";
		//				//lEntityToAdd.mSecondaryObject.renderer.material.color = Color.white;
		//				SetColour(lEntityToAdd.mSecondaryObject,Color.white);
		//
		//				lEntityToAdd.mSecondaryObject.layer = mnHoloLayer;

		//
		//				if (lEntity.mType == eSegmentEntity.Conveyor)
		//				{
		//
		//					//	lEntityToAdd.mVisualisationObject.transform.localScale = new Vector3(1.0f, 0.2f, 1.0f);
		//				}
	}


    bool mbHoloDirty = true;
    /// <summary>
    /// Called when this entity has added a holobase machine entity with the RequiresUpdates flag
    /// </summary>
    /// <param name="holobase">Holobase.</param>
    /// <param name="holoMachineEntity">Holo machine entity.</param>
    public override void HolobaseUpdate(Holobase holobase, HoloMachineEntity holoMachineEntity)
	{
        if (mbHoloDirty == false) return;
        mbHoloDirty = false;

        if (mCarriedCube == eCubeTypes.NULL && mCarriedItem == null) 
		{
            if (holoMachineEntity.VisualisationObjects[1].activeSelf == true) holoMachineEntity.VisualisationObjects[1].SetActive(false);
			//lMachine.mVisualisationObject.renderer.material.color = Color.gray;
			holobase.SetColour(holoMachineEntity.VisualisationObjects[0], Color.gray);
		}
		else
		{
            //To consider, move the carried cube
            if (holoMachineEntity.VisualisationObjects[1].activeSelf == false) holoMachineEntity.VisualisationObjects[1].SetActive(true);
			//lMachine.mVisualisationObject.renderer.material.color = Color.white;
			holobase.SetColour(holoMachineEntity.VisualisationObjects[0], Color.white);
		}
		if (mrBlockedTime > 5.0f) holobase.SetColour(holoMachineEntity.VisualisationObjects[0], Color.red);
        if (mbConveyorFrozen) holobase.SetColour(holoMachineEntity.VisualisationObjects[0], Color.blue);
        //lMachine.mVisualisationObject.renderer.material.color = Color.red;
    }	

	/// <summary>
	/// Allows a consumer to take a look at the item or cube currently held by the supplied.
	/// An item or cube will only be returned if it is ready to be taken.
	/// </summary>
	/// <returns><c>true</c>, if an item is available, <c>false</c> otherwise.</returns>
	/// <param name="sourceEntity">Source entity.</param>
	/// <param name="item">Item.</param>
	/// <param name="cubeType">Cube type.</param>
	/// <param name="cubeValue">Cube value.</param>
	public bool PeekItem(StorageUserInterface sourceEntity, out ItemBase item, out ushort cubeType, out ushort cubeValue)
	{
        if ((sourceEntity as SegmentEntity).mType == eSegmentEntity.Zipper_Merge)
        {
            if (mbItemHasBeenConverted == false) 
            {
                item = null;
                cubeValue = 0;
                cubeType = eCubeTypes.NULL;
                return false;
            }
        }

        if (IsCrafter())
        {
            if (mbItemHasBeenConverted == false)
            {
                item = null;
                cubeValue = 0;
                cubeType = eCubeTypes.NULL;
                return false;
            }
        }

        //if (!mbReadyToConvey && mrCarryTimer <= 0.0f)
        //if (mbItemHasBeenConverted == false) ConvertCargo();
        //PeekItem should return false is mbItemHasBeenConverted == false ?


		if (mCarriedItem != null)
		{
			item = mCarriedItem;
			cubeType = eCubeTypes.NULL;
			cubeValue = 0;
		}
		else if (mCarriedCube != eCubeTypes.NULL)
		{
			item = null;
			cubeType = mCarriedCube;
			cubeValue = mCarriedValue;
		}
		else
		{
			item = null;
			cubeType = eCubeTypes.NULL;
			cubeValue = 0;
			return false;
		}

		// If we get to here we have an item or cube ready to hand off, but first check the entity asking for it is in front of us.
		if (sourceEntity != null && sourceEntity is SegmentEntity)
		{
			SegmentEntity sourceSegmentEntity = (SegmentEntity)sourceEntity;

			long checkX = (this.mnX + (long)mForwards.x);
			long checkY = (this.mnY + (long)mForwards.y);
			long checkZ = (this.mnZ + (long)mForwards.z);

			if (sourceSegmentEntity.mnX != checkX || sourceSegmentEntity.mnY != checkY || sourceSegmentEntity.mnZ != checkZ)
			{
				// This entity is not in front of us, we will not be willing to deliver to it.
				item = null;
				cubeType = eCubeTypes.NULL;
				cubeValue = 0;
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Attempts to take an item or cube from the consumer of this interface.
	/// If this is successful <c>true</c> will be returned and the item will be removed from the consumer.
	/// </summary>
	/// <returns><c>true</c>, if the item was taken successful, <c>false</c> otherwise.</returns>
	/// <param name="sourceEntity">Destination entity.</param>
	/// <param name="item">Item.</param>
	/// <param name="cubeType">Cube type.</param>
	/// <param name="cubeValue">Cube value.</param>
	/// <param name="sendImmediateNetworkUpdate">Sends an immediate network update.</param>
	public bool TryTakeItem(StorageUserInterface sourceEntity, out ItemBase item, out ushort cubeType, out ushort cubeValue, bool sendImmediateNetworkUpdate)
	{
        //if (!mbReadyToConvey && mrCarryTimer <= 0.0f)
        //if (mbItemHasBeenConverted == false) ConvertCargo();
        //PeekItem should return false is mbItemHasBeenConverted == false ?

		if (mCarriedItem != null)
		{
			item = mCarriedItem;
			cubeType = eCubeTypes.NULL;
			cubeValue = 0;
		}
		else if (mCarriedCube != eCubeTypes.NULL)
		{
			item = null;
			cubeType = mCarriedCube;
			cubeValue = mCarriedValue;
		}
		else
		{
			item = null;
			cubeType = eCubeTypes.NULL;
			cubeValue = 0;
			return false;
		}

		// If we get to here we have an item or cube ready to hand off, but first check the entity asking for it is in front of us.
		if (sourceEntity != null && sourceEntity is SegmentEntity)
		{
			SegmentEntity sourceSegmentEntity = (SegmentEntity)sourceEntity;

			long checkX = (this.mnX + (long)mForwards.x);
			long checkY = (this.mnY + (long)mForwards.y);
			long checkZ = (this.mnZ + (long)mForwards.z);

			if (sourceSegmentEntity.mnX != checkX || sourceSegmentEntity.mnY != checkY || sourceSegmentEntity.mnZ != checkZ)
			{
				// This entity is not in front of us, we will not be willing to deliver to it.
				item = null;
				cubeType = eCubeTypes.NULL;
				cubeValue = 0;
				return false;
			}
		}

		ClearConveyor();

		if (sendImmediateNetworkUpdate)
		{
			RequestImmediateNetworkUpdate();
		}

		return true;
	}

	/// <summary>
	/// Attempts to deliver the specified item or cube from the source entity to the consumer
	/// of this interface. If the delivery is successful <c>true</c> will be returned.
	/// </summary>
	/// <returns><c>true</c>, if delivery of item was successful, <c>false</c> otherwise.</returns>
	/// <param name="sourceEntity">Source entity.</param>
	/// <param name="item">Item.</param>
	/// <param name="cubeType">Cube type.</param>
	/// <param name="cubeValue">Cube value.</param>
	/// <param name="sendImmediateNetworkUpdate">Sends an immediate network update.</param>
	public bool TryDeliverItem(StorageUserInterface sourceEntity, ItemBase item, ushort cubeType, ushort cubeValue, bool sendImmediateNetworkUpdate)
	{
		if (mbReadyToConvey == false)
			return false;

		if (IsCarryingCargo())
			return false;

		if (sourceEntity != null&& sourceEntity is SegmentEntity)
		{
			SegmentEntity sourceSegmentEntity = (SegmentEntity)sourceEntity;

			long checkX = (this.mnX + (long)mForwards.x);
			long checkY = (this.mnY + (long)mForwards.y);
			long checkZ = (this.mnZ + (long)mForwards.z);

			if (sourceSegmentEntity.mnX == checkX && sourceSegmentEntity.mnY == checkY && sourceSegmentEntity.mnZ == checkZ)
			{
				// This entity is directly in front of us, we will not be willing to deliver to it as we'd just pass it straight back again.
				return false;
			}
		}

		if (item != null)
		{
			AddItem(item);
		}
		else if (cubeType != 0)
		{
			AddCube(cubeType, cubeValue, 1.0f); // TODO: Work out where to place it based on the direction it has come from?
		}
		else
		{
			return false; // No item!
		}

		if (sendImmediateNetworkUpdate)
		{
			RequestImmediateNetworkUpdate();
		}

		return true;
	}

    void UpdateInstancedBase()
    {
        if (mnInstancedID == -1) return;

        Vector3 lUnityPos = WorldScript.instance.mPlayerFrustrum.GetCoordsToUnity(this.mnX, this.mnY, this.mnZ);
        lUnityPos.x += 0.5f;
        lUnityPos.y += 0.5f;
        lUnityPos.z += 0.5f;

        lUnityPos += mUp * -0.2174988f;//fu andy

        Quaternion lQuat = SegmentCustomRenderer.GetRotationQuaternion(mFlags);

        InstanceManager.instance.maSimpleInstancers[mnInstancerType].SetMatrixQuat(mnInstancedID,lUnityPos,lQuat,Vector3.one);


    }
}

