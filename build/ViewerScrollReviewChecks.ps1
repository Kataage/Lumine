# Shared five-item human checklist for final physical Windows scroll quality.
# This is review data only: its presence never establishes a GPU present,
# rendered bitmap readiness or product-owner signoff.
function New-ViewerScrollReviewChecks {
    @(
        [ordered]@{ id='gridSmallForward'; title='Grid: one normal wheel notch'; instruction='Let Grid settle, then scroll one notch. Look for a blank new tile strip.'; status='pending' },
        [ordered]@{ id='gridBurstReverse'; title='Grid: four forward, four reverse, two forward'; instruction='Scroll Grid 4 notches down, 4 up, 2 down. Check for blank strips or freezes.'; status='pending' },
        [ordered]@{ id='listSmallForward'; title='List: one normal wheel notch'; instruction='Switch to normal-density List, let it settle, scroll one notch. Check for blank rows.'; status='pending' },
        [ordered]@{ id='listBurstReverse'; title='List: four forward, four reverse, two forward'; instruction='Scroll List 4 down, 4 up, 2 down without artificial pauses. Watch the fourth forward notch.'; status='pending' },
        [ordered]@{ id='jumpRecovery'; title='Far jump followed by ordinary scroll'; instruction='Jump far away, wait for the new viewport to settle, then scroll normally and reverse. Cold distant images may take time to load.'; status='pending' }
    )
}
