# Pure acceptance verdicts for the single-session Issue #635 / #634 review.
# No application, image, GPU, filesystem or automation event can mark a human
# answer "pass". Unknown/incomplete checks must fail closed.
function Get-ViewerScrollOwnerDecision {
    param(
        [Parameter(Mandatory = $true)][object[]]$Checks,
        [bool]$SoftwareRendererOverride = $false
    )

    $ids = @('gridSmallForward', 'gridBurstReverse', 'listSmallForward',
             'listBurstReverse', 'jumpRecovery')
    if ($Checks.Count -ne $ids.Count -or
        (@($Checks | ForEach-Object { $_.id }) -join ',') -ne ($ids -join ',')) {
        throw 'Physical scroll review needs all five canonical checks.'
    }
    foreach ($check in $Checks) {
        if ($check.status -notin @('pass', 'fail', 'pending')) {
            throw "Invalid physical scroll review status: $($check.status)"
        }
    }

    if (@($Checks | Where-Object { $_.status -eq 'fail' }).Count -gt 0) {
        return 'fail'
    }
    if (@($Checks | Where-Object { $_.status -eq 'pending' }).Count -gt 0) {
        return 'pending'
    }
    if ($SoftwareRendererOverride) {
        return 'not-qualified-software-renderer'
    }
    return 'owner-reported-pass'
}

function Get-CombinedOwnerReviewDecision {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('pass', 'fail', 'pending')][string]$ProductManualDecision,
        [bool]$IncludeViewerScrollReview = $false,
        [ValidateSet('not-requested', 'owner-reported-pass', 'fail', 'pending',
                     'not-qualified-software-renderer')]
        [string]$ViewerScrollManualDecision = 'not-requested'
    )

    if (-not $IncludeViewerScrollReview -and
        $ViewerScrollManualDecision -ne 'not-requested') {
        throw 'Unrequested physical review cannot provide an acceptance verdict.'
    }
    if ($IncludeViewerScrollReview -and
        $ViewerScrollManualDecision -eq 'not-requested') {
        throw 'Consolidated physical review is missing its verdict.'
    }
    if ($ProductManualDecision -eq 'fail' -or
        ($IncludeViewerScrollReview -and $ViewerScrollManualDecision -eq 'fail')) {
        return 'fail'
    }
    if ($ProductManualDecision -ne 'pass' -or
        ($IncludeViewerScrollReview -and
         $ViewerScrollManualDecision -ne 'owner-reported-pass')) {
        return 'automated-pass-manual-review-required'
    }
    return 'pass'
}
