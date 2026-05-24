using System;
using VRC.SDK3.Avatars.Components;

namespace ASMLite.Editor
{
    internal readonly struct ASMLiteDirectDeliveryTransactionPlan
    {
        internal ASMLiteDirectDeliveryTransactionPlan(
            ASMLiteInstallationState expectedDetachedState,
            string vendorizedDir,
            bool retargetDescriptorToVendorized,
            ASMLiteLifecycleTransactionTestFailurePoint verifyFailurePoint,
            string executeFailureRemediation,
            string afterApplyFailureMessage,
            string afterApplyFailureRemediation,
            string verifyFailureMessage,
            string verifyFailureRemediation,
            string verificationFailureRemediation,
            bool verifyFailureContextUsesVendorizedDir)
        {
            ExpectedDetachedState = expectedDetachedState;
            VendorizedDir = NormalizeOptionalPath(vendorizedDir);
            RetargetDescriptorToVendorized = retargetDescriptorToVendorized;
            VerifyFailurePoint = verifyFailurePoint;
            ExecuteFailureRemediation = executeFailureRemediation ?? string.Empty;
            AfterApplyFailureMessage = afterApplyFailureMessage ?? string.Empty;
            AfterApplyFailureRemediation = afterApplyFailureRemediation ?? string.Empty;
            VerifyFailureMessage = verifyFailureMessage ?? string.Empty;
            VerifyFailureRemediation = verifyFailureRemediation ?? string.Empty;
            VerificationFailureRemediation = verificationFailureRemediation ?? string.Empty;
            VerifyFailureContextUsesVendorizedDir = verifyFailureContextUsesVendorizedDir;
        }

        internal ASMLiteInstallationState ExpectedDetachedState { get; }
        internal string VendorizedDir { get; }
        internal bool RetargetDescriptorToVendorized { get; }
        internal ASMLiteLifecycleTransactionTestFailurePoint VerifyFailurePoint { get; }
        internal string ExecuteFailureRemediation { get; }
        internal string AfterApplyFailureMessage { get; }
        internal string AfterApplyFailureRemediation { get; }
        internal string VerifyFailureMessage { get; }
        internal string VerifyFailureRemediation { get; }
        internal string VerificationFailureRemediation { get; }
        internal bool VerifyFailureContextUsesVendorizedDir { get; }

        private static string NormalizeOptionalPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace('\\', '/').TrimEnd('/');
        }
    }

    internal readonly struct ASMLiteDirectDeliveryTransactionResult
    {
        private ASMLiteDirectDeliveryTransactionResult(
            bool success,
            ASMLiteLifecycleTransactionStage failedStage,
            string detail,
            string message,
            string contextPath,
            string remediation,
            ASMLiteInstallationState expectedDetachedState)
        {
            Success = success;
            FailedStage = failedStage;
            Detail = detail ?? string.Empty;
            Message = message ?? string.Empty;
            ContextPath = contextPath ?? string.Empty;
            Remediation = remediation ?? string.Empty;
            ExpectedDetachedState = expectedDetachedState;
        }

        internal bool Success { get; }
        internal ASMLiteLifecycleTransactionStage FailedStage { get; }
        internal string Detail { get; }
        internal string Message { get; }
        internal string ContextPath { get; }
        internal string Remediation { get; }
        internal ASMLiteInstallationState ExpectedDetachedState { get; }

        internal static ASMLiteDirectDeliveryTransactionResult Pass(
            string detail,
            ASMLiteInstallationState expectedDetachedState)
        {
            return new ASMLiteDirectDeliveryTransactionResult(
                success: true,
                failedStage: ASMLiteLifecycleTransactionStage.Verify,
                detail: detail,
                message: string.Empty,
                contextPath: string.Empty,
                remediation: string.Empty,
                expectedDetachedState: expectedDetachedState);
        }

        internal static ASMLiteDirectDeliveryTransactionResult Fail(
            ASMLiteLifecycleTransactionStage failedStage,
            string message,
            string contextPath,
            string remediation,
            ASMLiteInstallationState expectedDetachedState)
        {
            return new ASMLiteDirectDeliveryTransactionResult(
                success: false,
                failedStage: failedStage,
                detail: string.Empty,
                message: message,
                contextPath: contextPath,
                remediation: remediation,
                expectedDetachedState: expectedDetachedState);
        }
    }

    internal static class ASMLiteDirectDeliveryTransaction
    {
        internal static ASMLiteDirectDeliveryTransactionResult Execute(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            ASMLiteDirectDeliveryTransactionPlan plan,
            Func<ASMLiteLifecycleTransactionTestFailurePoint, bool> shouldFailForTesting)
        {
            shouldFailForTesting = shouldFailForTesting ?? (_ => false);
            if (!ASMLiteBuilder.TryDetachToDirectDelivery(component, out string detail))
            {
                return ASMLiteDirectDeliveryTransactionResult.Fail(
                    ASMLiteLifecycleTransactionStage.Execute,
                    detail,
                    avatar != null ? avatar.gameObject.name : "avatar",
                    plan.ExecuteFailureRemediation,
                    plan.ExpectedDetachedState);
            }

            if (shouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.AfterDetachExecute))
            {
                return ASMLiteDirectDeliveryTransactionResult.Fail(
                    ASMLiteLifecycleTransactionStage.Execute,
                    plan.AfterApplyFailureMessage,
                    avatar != null ? avatar.gameObject.name : "avatar",
                    plan.AfterApplyFailureRemediation,
                    plan.ExpectedDetachedState);
            }

            if (plan.RetargetDescriptorToVendorized)
            {
                var descriptorRetargetResult = ASMLiteGeneratedAssetMirrorService.RetargetAvatarGeneratedAssetsToVendorized(avatar, plan.VendorizedDir);
                if (!descriptorRetargetResult.Success)
                {
                    return ASMLiteDirectDeliveryTransactionResult.Fail(
                        ASMLiteLifecycleTransactionStage.Execute,
                        descriptorRetargetResult.Message,
                        descriptorRetargetResult.ContextPath,
                        descriptorRetargetResult.Remediation,
                        plan.ExpectedDetachedState);
                }
            }

            string verifyFailureMessage = string.Empty;
            string verifyFailureContext = string.Empty;
            if (shouldFailForTesting(plan.VerifyFailurePoint)
                || !ASMLiteLifecycleVerification.VerifyDirectDeliveryState(
                    avatar,
                    plan.ExpectedDetachedState,
                    plan.VendorizedDir,
                    out verifyFailureMessage,
                    out verifyFailureContext))
            {
                return ASMLiteDirectDeliveryTransactionResult.Fail(
                    ASMLiteLifecycleTransactionStage.Verify,
                    shouldFailForTesting(plan.VerifyFailurePoint) ? plan.VerifyFailureMessage : verifyFailureMessage,
                    shouldFailForTesting(plan.VerifyFailurePoint) ? VerificationFailureContext(avatar, plan) : verifyFailureContext,
                    shouldFailForTesting(plan.VerifyFailurePoint) ? plan.VerifyFailureRemediation : plan.VerificationFailureRemediation,
                    plan.ExpectedDetachedState);
            }

            return ASMLiteDirectDeliveryTransactionResult.Pass(detail, plan.ExpectedDetachedState);
        }

        private static string VerificationFailureContext(VRCAvatarDescriptor avatar, ASMLiteDirectDeliveryTransactionPlan plan)
        {
            return plan.VerifyFailureContextUsesVendorizedDir
                ? plan.VendorizedDir
                : avatar != null ? avatar.gameObject.name : "avatar";
        }
    }
}
