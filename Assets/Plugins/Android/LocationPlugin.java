package com.triosoft.locationplugin;

import android.content.Intent;
import android.provider.Settings;
import android.app.Activity;
import android.util.Log;

public class LocationPlugin {

    // Opens device Location (GPS) settings
    public static void OpenLocationSettings(Activity activity) {
        try {
            Intent intent = new Intent(Settings.ACTION_LOCATION_SOURCE_SETTINGS);
            activity.startActivity(intent);
        } catch (Exception e) {
            Log.e("LocationPlugin", "Error opening location settings: " + e.getMessage());
        }
    }
}
