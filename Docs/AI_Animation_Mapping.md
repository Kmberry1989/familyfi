# AI Animation Mapping

This document lists fighter state names and their matched shared AnimationLibrary (.glb) fallbacks that were added to fighter scenes.

- Idle: Bouncing Fight Idle.glb
- Walk_Forward: Walking.glb
- Walk_Back: Walking Backward.glb
- Crouch_Idle: (closest: Crouched Walking.glb / Crouch To Stand.glb)
- Step_Back: Step Backward.glb
- Punch_A: Punching (1).glb
- Punch_Heavy: Punching 2.glb
- Kick_A: Kicking 3.glb
- Kick_Heavy: Kicking 4.glb / Hurricane Kick.glb
- Fireball: Fireball.glb
- FlashKick: Flying Bicycle Kick.glb / Flying Kick.glb
- Hit_Light: (closest: Stumble Backwards.glb)
- Hit_Stun (Dizzy): Dizzy Idle.glb
- Hit_Heavy: Knocked Out (1).glb / Dying.glb
- GetUp: Stand Up.glb
- Block (High/Low/Air): Blocking.glb
- Death: Dying.glb / Defeated.glb

Notes:
- Shared `AnimationLibrary` references were added to fighter scenes under `Fighters/*/*.tscn` to make these animations available at load time.
- Runtime injection (`GameManager.InjectSharedAnimations`) still provides additional fallbacks if a fighter's `AnimationLibrary` lacks an animation at runtime.

If you want, I can also add explicit `AnimationPlayer` library mappings inside each scene to alias the animation names to the `AnimationPlayer` entry used by the animator; tell me which fighters to target next.
