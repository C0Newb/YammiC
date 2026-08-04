package co.cnewb.yammic.ui.navigation

import android.content.Context
import co.cnewb.yammic.R
import co.cnewb.yammic.ymcsdk.navigation.NavigationState

fun describeNavigationState(
    context: Context,
    state: NavigationState,
): String =
    when (state) {
        is NavigationState.Idle -> context.getString(R.string.navigation_status_inactive)
        is NavigationState.Connecting -> context.getString(R.string.navigation_status_connecting)
        is NavigationState.Active -> context.getString(R.string.navigation_status_active, state.fps)
        is NavigationState.Error -> context.getString(R.string.navigation_status_error, state.message)
    }
