package de.lifedeck.tablet;

import android.app.Activity;
import android.content.SharedPreferences;
import android.content.pm.ActivityInfo;
import android.graphics.Color;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.os.Bundle;
import android.os.Build;
import android.os.Environment;
import android.view.Gravity;
import android.view.View;
import android.view.Window;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TableLayout;
import android.widget.TableRow;
import android.widget.TextView;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.BufferedWriter;
import java.io.File;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.net.ServerSocket;
import java.net.Socket;
import java.util.ArrayList;
import java.util.List;

public class MainActivity extends Activity {
    private static final String TAG = "LifeDeck";
    private static final int PORT = 24680;
    private static final String PREFS = "lifedeck_settings";
    private static final String PREF_ROTATE = "rotate_180";

    private LinearLayout root;
    private TextView pageTitle;

    private final List<DeckButton> buttons = new ArrayList<DeckButton>();
    private volatile BufferedWriter writer;
    private volatile boolean running = true;

    private String currentPage = "main";
    private int pageIndex = 0;
    private int pageCount = 1;
    private boolean rotate180 = false;

    static class DeckButton {
        String id;
        String title;
        String color;
        String icon;
        String displayMode;

        DeckButton(String id, String title, String color) {
            this(id, title, color, "", "imageText");
        }

        DeckButton(String id, String title, String color, String icon, String displayMode) {
            this.id = id;
            this.title = title;
            this.color = color;
            this.icon = icon;
            this.displayMode = displayMode;
        }
    }

    @Override
    public void onCreate(Bundle b) {
        super.onCreate(b);

        requestWindowFeature(Window.FEATURE_NO_TITLE);
        getWindow().setFlags(
                WindowManager.LayoutParams.FLAG_FULLSCREEN,
                WindowManager.LayoutParams.FLAG_FULLSCREEN
        );

        rotate180 = getSharedPreferences(PREFS, MODE_PRIVATE).getBoolean(PREF_ROTATE, false);
        applyOrientation();
        applyStableFullscreen();

        buildUi();
        showWaitingLayout();

        new Thread(new ServerLoop()).start();
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) applyStableFullscreen();
    }

    @Override
    protected void onDestroy() {
        running = false;
        super.onDestroy();
    }

    private int dp(int value) {
        float density = getResources().getDisplayMetrics().density;
        return (int) (value * density + 0.5f);
    }

    private void applyOrientation() {
        if (rotate180) setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_REVERSE_LANDSCAPE);
        else setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE);
    }

    private void toggleRotation() {
        rotate180 = !rotate180;
        SharedPreferences.Editor editor = getSharedPreferences(PREFS, MODE_PRIVATE).edit();
        editor.putBoolean(PREF_ROTATE, rotate180);
        editor.commit();
        applyOrientation();
        applyStableFullscreen();
    }

    private void applyStableFullscreen() {
        if (Build.VERSION.SDK_INT >= 16) {
            getWindow().getDecorView().setSystemUiVisibility(
                    View.SYSTEM_UI_FLAG_FULLSCREEN | View.SYSTEM_UI_FLAG_LAYOUT_STABLE
            );
        }
    }

    private void buildUi() {
        root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(Color.rgb(18, 18, 18));
        setContentView(root);
    }

    private void showWaitingLayout() {
        currentPage = "waiting";
        pageIndex = 0;
        pageCount = 1;
        buttons.clear();
        for (int i = 1; i <= 12; i++) buttons.add(new DeckButton("empty_" + i, "Warte\n" + i, "#303030"));
        renderButtons();
    }

    private void renderButtons() {
        root.removeAllViews();
        applyStableFullscreen();

        LinearLayout header = new LinearLayout(this);
        header.setOrientation(LinearLayout.HORIZONTAL);
        header.setGravity(Gravity.CENTER_VERTICAL);
        header.setPadding(dp(6), dp(5), dp(34), dp(5));
        header.setBackgroundColor(Color.rgb(25, 25, 25));

        Button prev = new Button(this);
        prev.setText("◀");
        prev.setTextSize(20);
        prev.setAllCaps(false);
        prev.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { sendSimple("pagePrev"); } });

        pageTitle = new TextView(this);
        pageTitle.setTextColor(Color.WHITE);
        pageTitle.setTextSize(17);
        pageTitle.setGravity(Gravity.CENTER);
        pageTitle.setText(currentPage + "  " + (pageIndex + 1) + "/" + pageCount);
        pageTitle.setOnLongClickListener(new View.OnLongClickListener() { public boolean onLongClick(View v) { toggleRotation(); return true; } });

        Button next = new Button(this);
        next.setText("▶");
        next.setTextSize(20);
        next.setAllCaps(false);
        next.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { sendSimple("pageNext"); } });

        header.addView(prev, new LinearLayout.LayoutParams(dp(54), dp(44)));
        header.addView(pageTitle, new LinearLayout.LayoutParams(0, dp(44), 1));
        header.addView(next, new LinearLayout.LayoutParams(dp(54), dp(44)));
        root.addView(header, new LinearLayout.LayoutParams(-1, -2));

        LinearLayout grid = new LinearLayout(this);
        grid.setOrientation(LinearLayout.VERTICAL);
        grid.setPadding(dp(6), dp(6), dp(6), dp(6));
        grid.setBaselineAligned(false);

        for (int r = 0; r < 3; r++) {
            LinearLayout row = new LinearLayout(this);
            row.setOrientation(LinearLayout.HORIZONTAL);
            row.setBaselineAligned(false);

            for (int c = 0; c < 4; c++) {
                final int index = r * 4 + c;
                View buttonView = index < buttons.size() ? createDeckButton(buttons.get(index), index) : createEmptyButton();
                LinearLayout.LayoutParams cellLp = new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.MATCH_PARENT, 1f);
                cellLp.setMargins(dp(3), dp(3), dp(3), dp(3));
                row.addView(buttonView, cellLp);
            }

            LinearLayout.LayoutParams rowLp = new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f);
            grid.addView(row, rowLp);
        }

        root.addView(grid, new LinearLayout.LayoutParams(-1, 0, 1));
    }

    private View createEmptyButton() {
        TextView tv = new TextView(this);
        tv.setBackgroundColor(Color.rgb(30, 30, 30));
        return tv;
    }

    private View createDeckButton(final DeckButton deckButton, final int index) {
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setGravity(Gravity.CENTER);
        box.setPadding(dp(3), dp(3), dp(3), dp(3));
        box.setClickable(true);
        try { box.setBackgroundColor(Color.parseColor(deckButton.color)); }
        catch (Exception ignored) { box.setBackgroundColor(Color.rgb(48, 48, 48)); }

        String mode = deckButton.displayMode == null ? "imageText" : deckButton.displayMode;
        boolean showImage = !"text".equals(mode);
        boolean showText = !"image".equals(mode);

        if (showImage) {
            ImageView image = new ImageView(this);
            image.setScaleType(ImageView.ScaleType.FIT_CENTER);
            image.setAdjustViewBounds(false);
            image.setPadding(dp(3), dp(3), dp(3), showText ? dp(1) : dp(3));
            Bitmap bmp = loadIcon(deckButton.icon);
            if (bmp != null) image.setImageBitmap(bmp);
            box.addView(image, new LinearLayout.LayoutParams(-1, 0, showText ? 4.5f : 1f));
        }

        if (showText) {
            TextView label = new TextView(this);
            label.setText(deckButton.title);
            label.setTextColor(Color.WHITE);
            label.setTextSize(showImage ? 12 : 18);
            label.setIncludeFontPadding(false);
            label.setGravity(Gravity.CENTER);
            label.setSingleLine(false);
            label.setPadding(dp(1), 0, dp(1), dp(1));
            box.addView(label, new LinearLayout.LayoutParams(-1, showImage ? dp(28) : 0, showImage ? 0f : 1f));
        }

        box.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { sendPress(deckButton.id, index); } });
        return box;
    }

    private Bitmap loadIcon(String iconName) {
        if (iconName == null || iconName.length() == 0) return null;
        try {
            File file = new File(Environment.getExternalStorageDirectory(), "LifeDeck/Assets/Icons/" + iconName);
            if (!file.exists()) return null;
            BitmapFactory.Options opts = new BitmapFactory.Options();
            opts.inPreferredConfig = Bitmap.Config.ARGB_8888;
            return BitmapFactory.decodeFile(file.getAbsolutePath(), opts);
        } catch (Exception e) {
            Log.e(TAG, "icon load failed", e);
            return null;
        }
    }

    private synchronized void sendLine(String s) {
        try {
            if (writer != null) { writer.write(s); writer.write("\n"); writer.flush(); }
            Log.i("LifeDeckEvent", s);
        } catch (Exception e) { Log.e(TAG, "send failed", e); }
    }

    private void sendSimple(String type) {
        try {
            JSONObject o = new JSONObject();
            o.put("type", type); o.put("page", currentPage); o.put("pageIndex", pageIndex);
            sendLine(o.toString());
        } catch (Exception e) { Log.e(TAG, "simple json failed", e); }
    }

    private void sendPress(String id, int index) {
        try {
            JSONObject o = new JSONObject();
            o.put("type", "press"); o.put("page", currentPage); o.put("button", id); o.put("index", index);
            sendLine(o.toString());
        } catch (Exception e) { Log.e(TAG, "press json failed", e); }
    }

    private void handleMessage(final String line) {
        runOnUiThread(new Runnable() { public void run() {
            try {
                JSONObject o = new JSONObject(line);
                String type = o.optString("type", "");
                if ("layout".equals(type)) {
                    currentPage = o.optString("page", "main");
                    pageIndex = o.optInt("pageIndex", 0);
                    pageCount = o.optInt("pageCount", 1);
                    JSONArray arr = o.getJSONArray("buttons");
                    buttons.clear();
                    for (int i = 0; i < arr.length(); i++) {
                        JSONObject b = arr.getJSONObject(i);
                        buttons.add(new DeckButton(
                                b.optString("id", "btn_" + i),
                                b.optString("title", "Button"),
                                b.optString("color", "#333333"),
                                b.optString("icon", ""),
                                b.optString("displayMode", "imageText")
                        ));
                    }
                    renderButtons();
                } else if ("ping".equals(type)) sendLine("{\"type\":\"pong\"}");
                else if ("rotate180".equals(type)) {
                    rotate180 = o.optBoolean("enabled", rotate180);
                    SharedPreferences.Editor editor = getSharedPreferences(PREFS, MODE_PRIVATE).edit();
                    editor.putBoolean(PREF_ROTATE, rotate180); editor.commit();
                    applyOrientation(); applyStableFullscreen();
                }
            } catch (Exception e) { Log.e(TAG, "bad message: " + line, e); }
        }});
    }

    private class ServerLoop implements Runnable {
        public void run() {
            while (running) {
                ServerSocket server = null;
                Socket socket = null;
                try {
                    server = new ServerSocket(PORT);
                    socket = server.accept();
                    writer = new BufferedWriter(new OutputStreamWriter(socket.getOutputStream(), "UTF-8"));
                    BufferedReader reader = new BufferedReader(new InputStreamReader(socket.getInputStream(), "UTF-8"));
                    JSONObject hello = new JSONObject();
                    hello.put("type", "hello"); hello.put("app", "LifeDeckTablet"); hello.put("protocol", 2); hello.put("android", Build.VERSION.RELEASE); hello.put("model", Build.MODEL);
                    sendLine(hello.toString());
                    String line;
                    while (running && (line = reader.readLine()) != null) handleMessage(line);
                } catch (Exception e) {
                    Log.e(TAG, "server error", e);
                    try { Thread.sleep(1000); } catch (Exception ignored) {}
                } finally {
                    writer = null;
                    try { if (socket != null) socket.close(); } catch (Exception ignored) {}
                    try { if (server != null) server.close(); } catch (Exception ignored) {}
                }
            }
        }
    }
}
