
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using LibSWBF2.Utils;
using System.Runtime.ExceptionServices;

using LibSWBF2.Enums;

public class PhxHover : PhxVehicle, IPhxTickable, IPhxTickablePhysics
{
    /*
    Pretty sure all the default values are correct.
    Unsure of how Length and Scale relate...
        The higher Length is, the more the string compresses and if 
        Length >= 2 * Scale, the hover will fall through terrain
    */

    protected class PhxHoverSpring
    {
        public float OmegaXFactor = 0f; // neg if z coord is pos 
        public float OmegaZFactor = 0f; // neg if x coord is neg
        public Vector3 Position;
        public float Length = 0f;
        public float Scale;

        public PhxHoverSpring(Vector4 PosScale)
        {
            Scale = PosScale.w;
            Position = new Vector3(PosScale.x, PosScale.y, PosScale.z);

			if (Position.x < -.001)
        	{
        		OmegaZFactor = -1f;
        	}
        	else if (Position.x > .001)
        	{
        		OmegaZFactor = 1f;
        	}

        	if (Position.z < -.001)
        	{
        		OmegaXFactor = 1f;
        	}
        	else if (Position.z > .001)
        	{
        		OmegaXFactor = -1f;
        	} 

        	Length = Scale;
        }

        public string ToString()
        {
            return String.Format("Position: {0}, Scale: {1}, Length: {2}", Position.ToString("F2"), Scale, Length);
        }
    }


    protected class PhxHoverWheel 
    {
        public Vector2 VelocityFactor = Vector2.zero;
        public Vector2 TurnFactor = Vector2.zero;

        public Material WheelMaterial;

        Vector2 TexOffset = Vector2.zero;
        int PropertyID;


        public PhxHoverWheel(Material WheelMat)
        {
            WheelMaterial = WheelMat;

            int BaseColorMapID = Shader.PropertyToID("_BaseColorMap");
            int UnlitColorMapID = Shader.PropertyToID("_UnlitColorMap");
            int MainTextureID = Shader.PropertyToID("_MainTexture");

            if (WheelMaterial.HasProperty(BaseColorMapID))
            {
                PropertyID = BaseColorMapID;
            }
            else if (WheelMaterial.HasProperty(UnlitColorMapID))
            {
                PropertyID = UnlitColorMapID;
            }
            else if (WheelMaterial.HasProperty(MainTextureID))
            {
                PropertyID = MainTextureID;
            }
            else 
            {
                PropertyID = -1;
            }
        }


        public string ToString()
        {
            return String.Format("WheelMaterial: {0} Vel Factors: {1} Turn Factors: {2}", WheelMaterial.name, VelocityFactor.ToString("F2"), TurnFactor.ToString("F2"));
        }

        public void Update(float deltaTime, float vel, float turn)
        {
            TexOffset += deltaTime * (vel * VelocityFactor + turn * TurnFactor);
            
            if (PropertyID != -1)
            {
                WheelMaterial.SetTextureOffset(PropertyID, TexOffset);
            }
        }
    }


    public class ClassProperties : PhxVehicleProperties
    {
        public PhxProp<float> Acceleration = new PhxProp<float>(5.0f);
        public PhxProp<float> Deceleration = new PhxProp<float>(5.0f);

        public PhxProp<float> ForwardSpeed = new PhxProp<float>(5.0f);
        public PhxProp<float> ReverseSpeed = new PhxProp<float>(5.0f);
        public PhxProp<float> StrafeSpeed = new PhxProp<float>(5.0f);

        public PhxProp<float> BoostSpeed = new PhxProp<float>(5.0f);
        public PhxProp<float> BoostAcceleration = new PhxProp<float>(5.0f);

        //  This is the altitude the hover SPAWNS at.  Actual hover height
        // is controlled by the spring settings!
        public PhxProp<float> SetAltitude = new PhxProp<float>(.5f);

        public PhxProp<float> LiftSpring = new PhxProp<float>(.5f);

        public PhxProp<float> GravityScale = new PhxProp<float>(.5f);

        public PhxProp<float> SpinRate = new PhxProp<float>(1.7f);
        public PhxProp<float> TurnRate = new PhxProp<float>(1.7f);

        public PhxProp<Vector2> PitchLimits = new PhxProp<Vector2>(Vector2.zero);  
        public PhxProp<Vector2> YawLimits = new PhxProp<Vector2>(Vector2.zero); 

        public PhxProp<AudioClip> EngineSound = new PhxProp<AudioClip>(null);

        public PhxPropertySection Wheels = new PhxPropertySection(
            "WHEELSECTION",
            ("WheelTexture",  new PhxProp<string>("")),
            ("WheelVelocToU", new PhxProp<float>(0f)),
            ("WheelOmegaToU", new PhxProp<float>(0f)),
            ("WheelVelocToV", new PhxProp<float>(0f)),
            ("WheelOmegaToV", new PhxProp<float>(0f))
        );

        public PhxProp<float> VelocitySpring = new PhxProp<float>(.5f);
        public PhxProp<float> VelocityDamp   = new PhxProp<float>(.5f);
        public PhxProp<float> OmegaXSpring   = new PhxProp<float>(.5f);
        public PhxProp<float> OmegaXDamp     = new PhxProp<float>(.5f);
        public PhxProp<float> OmegaZSpring   = new PhxProp<float>(1.7f);
        public PhxProp<float> OmegaZDamp     = new PhxProp<float>(1.7f);
    }


    Rigidbody Body;

    PhxHoverMainSection DriverSection = null;
    PhxHover.ClassProperties H;


    // Poser for movement anims
    PhxPoser Poser;

    // Springs for precise movement
    List<PhxHoverSpring> Springs;

    // For scrolling-texture wheels
    List<PhxHoverWheel> Wheels;

    AudioSource AudioAmbient;




    // Paired object with kinematic rigidbody and SO collision
    // Used to isolate concave collision so non-kinematic physics
    // can be used on vehicle.
    GameObject SOColliderObject;

    // Just for quick editor debugging
    [Serializable]
    public class SpringForce 
    {
    	public float VelForce;
    	public float VelDamp;

    	public float XRot;
    	public float XDamp;

    	public float ZRot;
    	public float ZDamp;

    	public float Penetration;
    }

    public List<SpringForce> SpringForces;



    List<PhxDamageEffect> DamageEffects;




    HashSet<Collider> ObjectColliders;



    // for debugging
    bool DBG = false;

    Vector3 startPos;

    public override void Init()
    {
        base.Init();

        CurHealth.Set(C.MaxHealth.Get());

        H = C as PhxHover.ClassProperties;
        if (H == null) return;

        transform.position += Vector3.up * H.SetAltitude;


        /*
        RIGIDBODY
        */

        Body = gameObject.AddComponent<Rigidbody>();
        Body.mass = H.GravityScale * 10f;
        Body.useGravity = true;
        Body.drag = 0.2f;
        Body.angularDrag = 10f;
        Body.interpolation = RigidbodyInterpolation.Interpolate;
        Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Body.isKinematic = false;

        // These get calculated automatically when adding colliders/children IF 
        // they are not set manually beforehand!!
        Body.centerOfMass = Vector3.zero;
        Body.inertiaTensor = new Vector3(1f,1f,1f);
        Body.inertiaTensorRotation = Quaternion.identity;


        // Will expand once sound loading is fixed
        AudioAmbient = gameObject.AddComponent<AudioSource>();
        AudioAmbient.spatialBlend = 1.0f;
        AudioAmbient.clip = H.EngineSound;
        AudioAmbient.pitch = 1.0f;
        AudioAmbient.volume = 0.5f;
        AudioAmbient.rolloffMode = AudioRolloffMode.Linear;
        AudioAmbient.minDistance = 2.0f;
        AudioAmbient.maxDistance = 30.0f;


        /*
        SECTIONS
        */

        Sections = new List<PhxVehicleSection>();

        var EC = H.EntityClass;
        EC.GetAllProperties(out uint[] properties, out string[] values);

        int i = 0;
        int TurretIndex = 1;
        while (i < properties.Length)
        {
            if (properties[i] == HashUtils.GetFNV("FLYERSECTION"))
            {
                if (values[i].Equals("BODY", StringComparison.OrdinalIgnoreCase))
                {
                    DriverSection = new PhxHoverMainSection(this);
                    DriverSection.InitManual(EC, i, "FLYERSECTION", "BODY");
                    Sections.Add(DriverSection);                
                }
                else 
                {
                    PhxVehicleTurret Turret = new PhxVehicleTurret(this, TurretIndex++);
                    Turret.InitManual(EC, i, "FLYERSECTION", values[i]);
                    Sections.Add(Turret);
                }
            }

            i++;
        }


        /*
        SPRINGS
        */

        i = 0;
        PhxHoverSpring CurrSpring = null;
        Springs = new List<PhxHoverSpring>();

        SpringForces = new List<SpringForce>();
        
        while (i < properties.Length)
        {
            if (properties[i] == HashUtils.GetFNV("AddSpringBody"))
            {
                CurrSpring = new PhxHoverSpring(PhxUtils.Vec4FromString(values[i]));
                Springs.Add(CurrSpring);
                
                /*
                // For visualization
                var sc = gameObject.AddComponent<SphereCollider>();
                sc.radius = CurrSpring.Scale;
                sc.center = CurrSpring.Position;
                sc.isTrigger = true;
                sc.enabled = false;
                */
                
                
                SpringForces.Add(new SpringForce());
            }
            else if (properties[i] == HashUtils.GetFNV("BodySpringLength"))
            {
                if (CurrSpring != null)
                {   
                    CurrSpring.Length = float.Parse(values[i], System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            else if (properties[i] == HashUtils.GetFNV("BodyOmegaXSpringFactor"))
            {
                if (CurrSpring != null)
                {
                    CurrSpring.OmegaXFactor = float.Parse(values[i], System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            else if (properties[i] == HashUtils.GetFNV("BodyOmegaZSpringFactor"))
            {
                if (CurrSpring != null)
                {
                    CurrSpring.OmegaZFactor = float.Parse(values[i], System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            i++;
        }
        

        /*
        POSER
        */

        if (H.AnimationName.Get() != "" && H.FinAnimation.Get() != "")
        {
            Poser = new PhxPoser(H.AnimationName.Get(), H.FinAnimation.Get(), transform);
        }


        /*
        WHEELS
        */

        foreach (Dictionary<string, IPhxPropRef> section in H.Wheels)
        {   
            // Named texture, actually refers to segment tag.
            section.TryGetValue("WheelTexture", out IPhxPropRef wheelNode);

            PhxProp<string> WheelNodeName = (PhxProp<string>) wheelNode;

            List<SWBFSegment> TaggedSegments = ModelMapping.GetSegmentsWithTag(WheelNodeName.Get());

            foreach (SWBFSegment Segment in TaggedSegments)
            {
                if (Segment.Node == null)
                {
                    Debug.LogErrorFormat("Tagged segment node is null for {0}", gameObject.name);
                    continue;
                }

                Renderer NodeRenderer = Segment.Node.GetComponent<Renderer>();
                if (NodeRenderer != null && Segment.Index < NodeRenderer.sharedMaterials.Length)
                {
                    if (Wheels == null)
                    {
                        Wheels = new List<PhxHoverWheel>();
                    }

                    PhxHoverWheel Wheel = new PhxHoverWheel(NodeRenderer.materials[Segment.Index]);

                    if (section.TryGetValue("WheelVelocToV", out IPhxPropRef V2VRef))
                    {
                        Wheel.VelocityFactor.y = ((PhxProp<float>) V2VRef).Get();
                    }

                    if (section.TryGetValue("WheelOmegaToV", out IPhxPropRef O2VRef))
                    {
                        Wheel.TurnFactor.y = ((PhxProp<float>) O2VRef).Get();
                    }

                    if (section.TryGetValue("WheelVelocToU", out IPhxPropRef V2URef))
                    {
                        Wheel.VelocityFactor.x = ((PhxProp<float>) V2URef).Get();
                    }

                    if (section.TryGetValue("WheelOmegaToU", out IPhxPropRef O2URef))
                    {
                        Wheel.TurnFactor.x = ((PhxProp<float>) O2URef).Get();
                    }

                    Wheels.Add(Wheel);
                }
            }
        }


        DamageEffects = new List<PhxDamageEffect>();
        PhxDamageEffect CurrDamageEffect = null;

        /*
        i = 0;
        while (i < properties.Length)
        {
            if (properties[i] == HashUtils.GetFNV("DamageStartPercent"))
            {
            	CurrDamageEffect = new PhxDamageEffect();
            	DamageEffects.Add(CurrDamageEffect);

            	CurrDamageEffect.DamageStartPercent = float.Parse(values[i]) / 100f;
            }
            else if (properties[i] == HashUtils.GetFNV("DamageStopPercent"))
            {
            	CurrDamageEffect.DamageStopPercent = float.Parse(values[i]) / 100f;
            }
            else if (properties[i] == HashUtils.GetFNV("DamageEffect"))
            {
            	CurrDamageEffect.Effect = SCENE.EffectsManager.LendEffect(values[i]);
            }
            else if (properties[i] == HashUtils.GetFNV("DamageAttachPoint"))
            {
            	CurrDamageEffect.DamageAttachPoint = UnityUtils.FindChildTransform(transform, values[i]);
            }

        	i++;
        }
        */
    }

    public override void Destroy()
    {
        
    }

    /*
    Update each section, pose if the poser is set, and wheel texture offset if applicable.
    */

    Vector2 WhellUVOffset = Vector2.zero;

    void UpdateState(float deltaTime)
    {
        if (SOColliderObject != null)
        {
            SOColliderObject.transform.position = transform.position;
            SOColliderObject.transform.rotation = transform.rotation;
        }


        /* 
        Sections
        */

        foreach (var section in Sections)
        {
            section.Tick(deltaTime);
        }


        /*
        Driver input
        */

        Vector3 Input = Vector3.zero;
        DriverController = DriverSection.GetController();
        if (DriverController != null) 
        {
            Input.x = DriverController.MoveDirection.x;
            Input.y = DriverController.MoveDirection.y;
            Input.z = DriverController.mouseX;
        }


        /*
        Vehicle pose
        */

        if (Poser != null)
        {
            float blend = 2f * deltaTime;

            if (Vector3.Magnitude(Input) < .001f)
            {
                Poser.SetState(PhxNinePoseState.Idle, blend);
            }
            else 
            {
	            if (Input.x > .01f)
	            {
	                Poser.SetState(PhxNinePoseState.StrafeRight, blend);           
	            }

	            if (Input.x < -.01f)
	            {
	                Poser.SetState(PhxNinePoseState.StrafeLeft, blend);            
	            }

	            if (Input.y < 0f) 
	            {
	                if (Input.z > .01f)
	                {
	                    Poser.SetState(PhxNinePoseState.BackwardsTurnLeft, blend);            
	                }
	                else if (Input.z < -.01f)
	                {
	                    Poser.SetState(PhxNinePoseState.BackwardsTurnRight, blend);            
	                }
	                else
	                {
	                    Poser.SetState(PhxNinePoseState.Backwards, blend);            
	                }
	            }
	            else
	            {
	                if (Input.z > .01f)
	                {
	                    Poser.SetState(PhxNinePoseState.ForwardTurnLeft, blend);            
	                }
	                else if (Input.z < -.01f)
	                {
	                    Poser.SetState(PhxNinePoseState.ForwardTurnRight, blend);            
	                }
	                else
	                {
	                    Poser.SetState(PhxNinePoseState.Forward, blend);            
	                }
	            }
        	}
        }


       /*
        Wheels
        */

        if (Wheels != null)
        {
            foreach (PhxHoverWheel Wheel in Wheels)
            {
                Wheel.Update(deltaTime, LocalVel.z, LocalAngVel.z);
            }
        }


        // Update damage effects
        //CurHealth.Set(Mathf.Clamp(CurHealth.Get() - ((deltaTime / 15f) * C.MaxHealth.Get()), 1f, C.MaxHealth.Get()));

        float HealthPercent = CurHealth.Get() / C.MaxHealth.Get();
        

        foreach (PhxDamageEffect DamageEffect in DamageEffects)
        {
        	if (HealthPercent < DamageEffect.DamageStartPercent && HealthPercent > DamageEffect.DamageStopPercent)
        	{
        		if (!DamageEffect.IsOn && DamageEffect.Effect != null)
        		{
        			Debug.LogFormat("Playing effect: {0} at node: {1}...", DamageEffect.Effect.EffectName, DamageEffect.DamageAttachPoint.name);
        			DamageEffect.IsOn = true;
        			DamageEffect.Effect.SetParent(DamageEffect.DamageAttachPoint);
        			DamageEffect.Effect.SetLocalTransform(Vector3.zero, Quaternion.identity);
        			DamageEffect.Effect.Play();
        		}
        	}
        	else 
        	{
        		if (DamageEffect.IsOn && DamageEffect.Effect != null)
        		{
        			Debug.LogFormat("Stopping effect: {0}...", DamageEffect.Effect.EffectName);

        			DamageEffect.IsOn = false;
        			DamageEffect.Effect.Stop();
        		}
        	}
        }
    }

    
    /* 
        Each spring is processed by shooting a ray from its origin via the vehicle's down
        vector.  If the ray hits another object and does so within the radius (Scale) of the
        spring, a seperate torque and force will be applied to the vehicle.

        The exact function of the various parameters is still unknown, ideally I'll have an 
        equation done/tested soon.  But this method produces gamelike behavior from the 
        parameters.

        Possible upgrades depend on performance trade off:
            
            - Cast spheres and compute penetration between colliders manually with 
            Physics.OverlapSphere and Physics.ComputePenetration.  Springs ingame are
            spherical, though I bet a downward raycast will be sufficient.  
            
        Confusion on parameters remains:

            - OmegaXSpringFactor is used to compensate for an imbalance of springs.  Eg, when two have
            negative z coords and one has positive, the latter spring will have a negative OmegaXSpringFactor (stap, speeders).
            The opposite is also true (AAT).  What do these values default to?  Are they calculated from the spring
            location?

            - Omega is typically used with angular quantities, so I assume the explicit references mean
            the engine computes an upward force and a torque seperately.

            - How are the various global spring parameters like LiftSpring/Damp and VelocitySpring/Damp used and
            combined with the local ones? 
    */ 

    int SpringUpdatesPerFrame = 2; 

    void UpdateSprings(float deltaTime)
    {
        Vector3 netForce = Vector3.zero;
        Vector3 netPos = Vector3.zero;

        LayerMask Mask = (1 << 11) | (1 << 12) | (1 << 13) | (1 << 14) | (1 << 15);

        // Set all colliders to RaycastIgnore
        ModelMapping.SetColliderLayerAll(2);


        for (int CurrSpringIndex = 0; CurrSpringIndex < Springs.Count; CurrSpringIndex++)
        {
            var CurrSpring = Springs[CurrSpringIndex];

            if (Physics.Raycast(transform.TransformPoint(CurrSpring.Position), -transform.up, out RaycastHit hit, CurrSpring.Scale, Mask, QueryTriggerInteraction.Ignore))
            {
            	float Penetration = (CurrSpring.Scale - hit.distance) / CurrSpring.Length;
                Penetration = Penetration > 1f ? 1f : Penetration;

                SpringForces[CurrSpringIndex].Penetration = Penetration;

                if (hit.collider.gameObject != gameObject && Penetration > 0f)
                {
                	// Check for imminent hard collision
                	// ...

                    // This is all pretty hand wavy, parameters are too sensitive
                    float XRotCoeff = 2f * H.OmegaXSpring * CurrSpring.OmegaXFactor;
                    float XDampRotCoeff = .3f * H.OmegaXDamp * -Vector3.Dot(Body.angularVelocity, transform.right);
                    Body.AddRelativeTorque(60f * Penetration * deltaTime * Vector3.right * (XRotCoeff + XDampRotCoeff), ForceMode.Acceleration);

                    SpringForces[CurrSpringIndex].XRot = XRotCoeff;
                    SpringForces[CurrSpringIndex].XDamp = XDampRotCoeff;

                    float ZRotCoeff = H.OmegaZSpring * CurrSpring.OmegaZFactor;
                    float ZRotDampCoeff = .5f * H.OmegaZDamp * -Vector3.Dot(Body.angularVelocity, transform.forward);
                    Body.AddRelativeTorque(80f * Penetration * deltaTime * Vector3.forward * (ZRotCoeff + ZRotDampCoeff), ForceMode.Acceleration);
                    
                    SpringForces[CurrSpringIndex].ZRot = ZRotCoeff;
                    SpringForces[CurrSpringIndex].ZDamp = ZRotDampCoeff;

                    Vector3 VelSpringForce = Vector3.up * H.VelocitySpring;
                    Vector3 VelDampForce = .3f * Vector3.up * H.VelocityDamp * -Body.velocity.y;
                    Body.AddForce(80f * Penetration * deltaTime * (VelSpringForce + VelDampForce), ForceMode.Acceleration);                

                    SpringForces[CurrSpringIndex].VelForce = VelSpringForce.y;
                    SpringForces[CurrSpringIndex].VelDamp = VelDampForce.y;
                }
                else 
                {
                    SpringForces[CurrSpringIndex].XRot = 0f;
                    SpringForces[CurrSpringIndex].XDamp = 0f;                	
                    SpringForces[CurrSpringIndex].ZRot = 0f;
                    SpringForces[CurrSpringIndex].ZDamp = 0f;
                    SpringForces[CurrSpringIndex].VelForce = 0f;
                    SpringForces[CurrSpringIndex].VelDamp = 0f;
                }  
            }
        }

        // Reset all colliders to their mask's layers
        ModelMapping.SetColliderLayerFromMaskAll();
    }




    Vector3 LocalVel = Vector3.zero;
    Vector3 LocalAngVel = Vector3.zero;
    PhxPawnController DriverController;

    void UpdatePhysics(float deltaTime)
    {
        LocalVel = transform.worldToLocalMatrix * Body.velocity;
        LocalAngVel = transform.worldToLocalMatrix * Body.angularVelocity;
    
        UpdateSprings(deltaTime);


        DriverController = DriverSection.GetController();
        if (DriverController == null) 
        {
            return;
        }

        // If we're moving, we Spin, if not we Turn
        float rotRate = Vector3.Magnitude(LocalVel) < .1f ? H.SpinRate : H.TurnRate;

        Quaternion deltaRotation = Quaternion.Euler(new Vector3(0f, 16f * rotRate * DriverController.mouseX, 0f) * deltaTime);
        Body.MoveRotation(Body.rotation * deltaRotation);

        float strafe = DriverController.MoveDirection.x;
        float drive  = DriverController.MoveDirection.y;

        float forwardForce, strafeForce;

        // If moving in opposite direction of current vel...
        if (LocalVel.z >= 0f && drive < 0f)
        {
            forwardForce = drive * H.Deceleration;
        }
        else
        {
            forwardForce = drive * H.Acceleration;
        }

        // ''
        if (LocalVel.x - strafe > LocalVel.x)
        {
            strafeForce = strafe * H.Deceleration;
        }
        else 
        {
            strafeForce = strafe * H.Acceleration;
        }

        // engine accel, don't add force here because we want to limit local velocity manually
        LocalVel += 2f * deltaTime * new Vector3(strafeForce, 0f, forwardForce) / H.GravityScale;

        // clamp speeds by ODF vals, for now doesn't damp
        LocalVel.x = Mathf.Clamp(LocalVel.x, -H.StrafeSpeed, H.StrafeSpeed);
        LocalVel.z = Mathf.Clamp(LocalVel.z, -H.ReverseSpeed, H.ForwardSpeed);

        Body.velocity = transform.localToWorldMatrix * LocalVel;
    }



    public override Vector3 GetCameraPosition()
    {
        return Sections[0].GetCameraPosition();
    }

    public override Quaternion GetCameraRotation()
    {
        return Sections[0].GetCameraRotation();
    }


    public override bool IncrementSlice(out float progress)
    {
        progress = SliceProgress;
        return false;
    }


    public void Tick(float deltaTime)
    {
        UnityEngine.Profiling.Profiler.BeginSample("Tick Hover");
        UpdateState(deltaTime);
        UnityEngine.Profiling.Profiler.EndSample();
    }

    public void TickPhysics(float deltaTime)
    {
        UnityEngine.Profiling.Profiler.BeginSample("Tick Hover Physics");
        UpdatePhysics(deltaTime);
        UnityEngine.Profiling.Profiler.EndSample();
    }
}