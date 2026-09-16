using System;
using FishNet.Serializing;
using Fragsurf.Movement;
using UnityEngine;

namespace GAUNTLET.Rollback {
    // Explicit wire contract. The manifest test rejects newly added MoveData
    // fields until they are included here or deliberately classified as bindings.
    public static class WorldMovementSerializer {
        public const ushort Version = 1;
        public static readonly string[] Fields = {
            "frame",
            "lastInput",
            "moveType",
            "viewTransformDefaultLocalPos",
            "origin",
            "viewAngles",
            "velocity",
            "forwardMove",
            "sideMove",
            "upMove",
            "surfaceFriction",
            "gravityFactor",
            "walkFactor",
            "verticalAxis",
            "horizontalAxis",
            "wishJump",
            "wishJumpDown",
            "lastConsumedJumpPressFrame",
            "crouching",
            "jumping",
            "crouchLerp",
            "renderCrouchLerp",
            "uncrouchDown",
            "maxStaminaBonus",
            "staminaRegenBonus",
            "stamina",
            "staminaRegenTimer",
            "dashTimer",
            "currentDashDuration",
            "dashCooldownTimer",
            "isDashing",
            "canAirDash",
            "wishDash",
            "lastConsumedDashPressFrame",
            "jumpCount",
            "jumpTimer",
            "slideRequested",
            "sliding",
            "wasSliding",
            "slideSpeedCurrent",
            "slideDirection",
            "slideDelay",
            "slopeLimit",
            "rigidbodyPushForce",
            "defaultHeight",
            "crouchingHeight",
            "crouchingSpeed",
            "toggleCrouch",
            "slidingEnabled",
            "laddersEnabled",
            "angledLaddersEnabled",
            "climbingLadder",
            "ladderNormal",
            "ladderDirection",
            "ladderClimbDir",
            "ladderVelocity",
            "underwater",
            "cameraUnderwater",
            "grounded",
            "groundedTemp",
            "fallingVelocity",
            "useStepOffset",
            "stepOffset",
            "meleeState",
            "meleeTimer",
            "meleeCooldownTimer",
            "wishMelee",
            "meleeHoldTimer",
            "hasHitTarget",
            "meleeHitResolved",
            "meleeHitTargetObjectId",
            "meleeHitResolveTick",
            "wishParry",
            "isParrying",
            "parryTimer",
            "parryCooldownTimer",
            "lastConsumedParryPressFrame",
            "dashStartedThisFrame",
            "doubleJumpedThisFrame",
            "meleeHitThisFrame",
            "meleeMinChargeReachedThisFrame",
            "meleeCooldownReadyThisFrame",
            "parryStartedThisFrame",
            "parrySuccessThisFrame",
            "gotParriedThisFrame",
            "parryCooldownReadyThisFrame",
        };

        public static void WriteWorldMovement(this Writer writer, MoveData value) {
            writer.WriteBoolean(value != null);
            if (value == null) return;
            writer.WriteUInt16(Version);
            writer.Write(value.frame);
            writer.Write(value.lastInput);
            writer.Write(value.moveType);
            writer.Write(value.viewTransformDefaultLocalPos);
            writer.Write(value.origin);
            writer.Write(value.viewAngles);
            writer.Write(value.velocity);
            writer.Write(value.forwardMove);
            writer.Write(value.sideMove);
            writer.Write(value.upMove);
            writer.Write(value.surfaceFriction);
            writer.Write(value.gravityFactor);
            writer.Write(value.walkFactor);
            writer.Write(value.verticalAxis);
            writer.Write(value.horizontalAxis);
            writer.Write(value.wishJump);
            writer.Write(value.wishJumpDown);
            writer.Write(value.lastConsumedJumpPressFrame);
            writer.Write(value.crouching);
            writer.Write(value.jumping);
            writer.Write(value.crouchLerp);
            writer.Write(value.renderCrouchLerp);
            writer.Write(value.uncrouchDown);
            writer.Write(value.maxStaminaBonus);
            writer.Write(value.staminaRegenBonus);
            writer.Write(value.stamina);
            writer.Write(value.staminaRegenTimer);
            writer.Write(value.dashTimer);
            writer.Write(value.currentDashDuration);
            writer.Write(value.dashCooldownTimer);
            writer.Write(value.isDashing);
            writer.Write(value.canAirDash);
            writer.Write(value.wishDash);
            writer.Write(value.lastConsumedDashPressFrame);
            writer.Write(value.jumpCount);
            writer.Write(value.jumpTimer);
            writer.Write(value.slideRequested);
            writer.Write(value.sliding);
            writer.Write(value.wasSliding);
            writer.Write(value.slideSpeedCurrent);
            writer.Write(value.slideDirection);
            writer.Write(value.slideDelay);
            writer.Write(value.slopeLimit);
            writer.Write(value.rigidbodyPushForce);
            writer.Write(value.defaultHeight);
            writer.Write(value.crouchingHeight);
            writer.Write(value.crouchingSpeed);
            writer.Write(value.toggleCrouch);
            writer.Write(value.slidingEnabled);
            writer.Write(value.laddersEnabled);
            writer.Write(value.angledLaddersEnabled);
            writer.Write(value.climbingLadder);
            writer.Write(value.ladderNormal);
            writer.Write(value.ladderDirection);
            writer.Write(value.ladderClimbDir);
            writer.Write(value.ladderVelocity);
            writer.Write(value.underwater);
            writer.Write(value.cameraUnderwater);
            writer.Write(value.grounded);
            writer.Write(value.groundedTemp);
            writer.Write(value.fallingVelocity);
            writer.Write(value.useStepOffset);
            writer.Write(value.stepOffset);
            writer.Write(value.meleeState);
            writer.Write(value.meleeTimer);
            writer.Write(value.meleeCooldownTimer);
            writer.Write(value.wishMelee);
            writer.Write(value.meleeHoldTimer);
            writer.Write(value.hasHitTarget);
            writer.Write(value.meleeHitResolved);
            writer.Write(value.meleeHitTargetObjectId);
            writer.Write(value.meleeHitResolveTick);
            writer.Write(value.wishParry);
            writer.Write(value.isParrying);
            writer.Write(value.parryTimer);
            writer.Write(value.parryCooldownTimer);
            writer.Write(value.lastConsumedParryPressFrame);
            writer.Write(value.dashStartedThisFrame);
            writer.Write(value.doubleJumpedThisFrame);
            writer.Write(value.meleeHitThisFrame);
            writer.Write(value.meleeMinChargeReachedThisFrame);
            writer.Write(value.meleeCooldownReadyThisFrame);
            writer.Write(value.parryStartedThisFrame);
            writer.Write(value.parrySuccessThisFrame);
            writer.Write(value.gotParriedThisFrame);
            writer.Write(value.parryCooldownReadyThisFrame);
        }

        public static MoveData ReadWorldMovement(this Reader reader) {
            if (!reader.ReadBoolean()) return null;
            ushort version = reader.ReadUInt16();
            if (version != Version) throw new InvalidOperationException($"Unsupported movement snapshot version {version}.");
            return new MoveData {
                frame = reader.Read<int>(),
                lastInput = reader.Read<InputFrame>(),
                moveType = reader.Read<MoveType>(),
                viewTransformDefaultLocalPos = reader.Read<Vector3>(),
                origin = reader.Read<Vector3>(),
                viewAngles = reader.Read<Vector3>(),
                velocity = reader.Read<Vector3>(),
                forwardMove = reader.Read<float>(),
                sideMove = reader.Read<float>(),
                upMove = reader.Read<float>(),
                surfaceFriction = reader.Read<float>(),
                gravityFactor = reader.Read<float>(),
                walkFactor = reader.Read<float>(),
                verticalAxis = reader.Read<float>(),
                horizontalAxis = reader.Read<float>(),
                wishJump = reader.Read<bool>(),
                wishJumpDown = reader.Read<bool>(),
                lastConsumedJumpPressFrame = reader.Read<int>(),
                crouching = reader.Read<bool>(),
                jumping = reader.Read<bool>(),
                crouchLerp = reader.Read<float>(),
                renderCrouchLerp = reader.Read<float>(),
                uncrouchDown = reader.Read<bool>(),
                maxStaminaBonus = reader.Read<float>(),
                staminaRegenBonus = reader.Read<float>(),
                stamina = reader.Read<float>(),
                staminaRegenTimer = reader.Read<float>(),
                dashTimer = reader.Read<float>(),
                currentDashDuration = reader.Read<float>(),
                dashCooldownTimer = reader.Read<float>(),
                isDashing = reader.Read<bool>(),
                canAirDash = reader.Read<bool>(),
                wishDash = reader.Read<bool>(),
                lastConsumedDashPressFrame = reader.Read<int>(),
                jumpCount = reader.Read<int>(),
                jumpTimer = reader.Read<float>(),
                slideRequested = reader.Read<bool>(),
                sliding = reader.Read<bool>(),
                wasSliding = reader.Read<bool>(),
                slideSpeedCurrent = reader.Read<float>(),
                slideDirection = reader.Read<Vector3>(),
                slideDelay = reader.Read<float>(),
                slopeLimit = reader.Read<float>(),
                rigidbodyPushForce = reader.Read<float>(),
                defaultHeight = reader.Read<float>(),
                crouchingHeight = reader.Read<float>(),
                crouchingSpeed = reader.Read<float>(),
                toggleCrouch = reader.Read<bool>(),
                slidingEnabled = reader.Read<bool>(),
                laddersEnabled = reader.Read<bool>(),
                angledLaddersEnabled = reader.Read<bool>(),
                climbingLadder = reader.Read<bool>(),
                ladderNormal = reader.Read<Vector3>(),
                ladderDirection = reader.Read<Vector3>(),
                ladderClimbDir = reader.Read<Vector3>(),
                ladderVelocity = reader.Read<Vector3>(),
                underwater = reader.Read<bool>(),
                cameraUnderwater = reader.Read<bool>(),
                grounded = reader.Read<bool>(),
                groundedTemp = reader.Read<bool>(),
                fallingVelocity = reader.Read<float>(),
                useStepOffset = reader.Read<bool>(),
                stepOffset = reader.Read<float>(),
                meleeState = reader.Read<MoveData.MeleeState>(),
                meleeTimer = reader.Read<float>(),
                meleeCooldownTimer = reader.Read<float>(),
                wishMelee = reader.Read<bool>(),
                meleeHoldTimer = reader.Read<float>(),
                hasHitTarget = reader.Read<bool>(),
                meleeHitResolved = reader.Read<bool>(),
                meleeHitTargetObjectId = reader.Read<int>(),
                meleeHitResolveTick = reader.Read<int>(),
                wishParry = reader.Read<bool>(),
                isParrying = reader.Read<bool>(),
                parryTimer = reader.Read<float>(),
                parryCooldownTimer = reader.Read<float>(),
                lastConsumedParryPressFrame = reader.Read<int>(),
                dashStartedThisFrame = reader.Read<bool>(),
                doubleJumpedThisFrame = reader.Read<bool>(),
                meleeHitThisFrame = reader.Read<bool>(),
                meleeMinChargeReachedThisFrame = reader.Read<bool>(),
                meleeCooldownReadyThisFrame = reader.Read<bool>(),
                parryStartedThisFrame = reader.Read<bool>(),
                parrySuccessThisFrame = reader.Read<bool>(),
                gotParriedThisFrame = reader.Read<bool>(),
                parryCooldownReadyThisFrame = reader.Read<bool>(),
            };
        }
    }
}

