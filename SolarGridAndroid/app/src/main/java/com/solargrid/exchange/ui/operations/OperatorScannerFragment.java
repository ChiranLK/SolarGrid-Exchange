package com.solargrid.exchange.ui.operations;

import android.Manifest;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Bundle;
import android.provider.Settings;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.TextView;

import androidx.activity.result.ActivityResultLauncher;
import androidx.activity.result.contract.ActivityResultContracts;
import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.appcompat.app.AlertDialog;
import androidx.camera.core.CameraSelector;
import androidx.camera.core.ExperimentalGetImage;
import androidx.camera.core.ImageAnalysis;
import androidx.camera.core.ImageProxy;
import androidx.camera.core.Preview;
import androidx.camera.lifecycle.ProcessCameraProvider;
import androidx.camera.view.PreviewView;
import androidx.core.content.ContextCompat;
import androidx.fragment.app.Fragment;
import androidx.lifecycle.ViewModelProvider;
import androidx.navigation.NavController;
import androidx.navigation.Navigation;

import com.google.common.util.concurrent.ListenableFuture;
import com.google.mlkit.vision.barcode.BarcodeScanner;
import com.google.mlkit.vision.barcode.BarcodeScannerOptions;
import com.google.mlkit.vision.barcode.BarcodeScanning;
import com.google.mlkit.vision.barcode.common.Barcode;
import com.google.mlkit.vision.common.InputImage;
import com.solargrid.exchange.R;
import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.SessionUser;
import com.solargrid.exchange.features.operations.CameraPermissionState;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.MainActivity;
import com.solargrid.exchange.ui.common.UiState;
import com.solargrid.exchange.ui.common.UiStateView;

import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

public final class OperatorScannerFragment extends Fragment {
    private static final String CAMERA_REQUESTED = "operator.cameraRequested";

    private final AtomicBoolean frameInFlight = new AtomicBoolean(false);
    private final AtomicBoolean scanCaptured = new AtomicBoolean(false);
    private final ActivityResultLauncher<String> cameraPermissionLauncher =
            registerForActivityResult(new ActivityResultContracts.RequestPermission(), granted -> {
                cameraRequested = true;
                renderPermissionState();
            });

    private OperatorTransactionViewModel viewModel;
    private PreviewView previewView;
    private UiStateView stateView;
    private View permissionPanel;
    private TextView permissionMessage;
    private Button requestPermission;
    private Button openSettings;
    private View scanInstruction;
    private ProcessCameraProvider cameraProvider;
    private BarcodeScanner barcodeScanner;
    private ExecutorService analysisExecutor;
    private boolean cameraRequested;
    private boolean operatorAuthorized;
    private boolean cameraWanted;
    private boolean cameraStartPending;

    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_operator_scanner, container, false);
        previewView = view.findViewById(R.id.operator_camera_preview);
        stateView = view.findViewById(R.id.operator_scanner_state);
        permissionPanel = view.findViewById(R.id.operator_camera_permission_panel);
        permissionMessage = view.findViewById(R.id.operator_camera_permission_message);
        requestPermission = view.findViewById(R.id.operator_camera_permission_request);
        openSettings = view.findViewById(R.id.operator_camera_settings);
        scanInstruction = view.findViewById(R.id.operator_scan_instruction);
        stateView.hide();
        cameraRequested = savedInstanceState != null
                && savedInstanceState.getBoolean(CAMERA_REQUESTED, false);

        SessionUser session = ((SolarGridApplication) requireActivity().getApplication())
                .getAppContainer()
                .getAuthRepository()
                .getStoredSession();
        if (session == null) {
            ((MainActivity) requireActivity()).handleAuthenticationExpiry();
            return view;
        }
        operatorAuthorized = session.isGridOperator();
        if (!operatorAuthorized) {
            permissionPanel.setVisibility(View.GONE);
            stateView.showError(new ApiError(
                    ApiError.Kind.FORBIDDEN, 403, getString(R.string.operator_only_screen)), null);
            return view;
        }

        BarcodeScannerOptions options = new BarcodeScannerOptions.Builder()
                .setBarcodeFormats(Barcode.FORMAT_QR_CODE)
                .build();
        barcodeScanner = BarcodeScanning.getClient(options);
        analysisExecutor = Executors.newSingleThreadExecutor();
        viewModel = new ViewModelProvider(requireActivity())
                .get(OperatorTransactionViewModel.class);

        requestPermission.setOnClickListener(ignored -> requestCameraPermission());
        openSettings.setOnClickListener(ignored -> openApplicationSettings());
        viewModel.getVerificationState().observe(getViewLifecycleOwner(), state -> {
            switch (state.getStatus()) {
                case IDLE:
                    stateView.hide();
                    if (hasCameraPermission()) {
                        showCamera();
                    }
                    break;
                case LOADING:
                    stopCamera();
                    stateView.showLoading(getString(R.string.verifying_qr));
                    break;
                case ERROR:
                    stopCamera();
                    if (state.getError() != null && state.getError().isAuthenticationExpired()) {
                        ((MainActivity) requireActivity()).handleAuthenticationExpiry();
                    } else {
                        navigateToVerification(view);
                    }
                    break;
                case SUCCESS:
                    stopCamera();
                    navigateToVerification(view);
                    break;
                default:
                    break;
            }
        });
        return view;
    }

    @Override
    public void onResume() {
        super.onResume();
        if (operatorAuthorized) {
            renderPermissionState();
        }
    }

    @Override
    public void onPause() {
        cameraWanted = false;
        stopCamera();
        super.onPause();
    }

    @Override
    public void onSaveInstanceState(@NonNull Bundle outState) {
        outState.putBoolean(CAMERA_REQUESTED, cameraRequested);
        super.onSaveInstanceState(outState);
    }

    @Override
    public void onDestroyView() {
        stopCamera();
        if (barcodeScanner != null) {
            barcodeScanner.close();
        }
        if (analysisExecutor != null) {
            analysisExecutor.shutdownNow();
        }
        barcodeScanner = null;
        analysisExecutor = null;
        previewView = null;
        stateView = null;
        permissionPanel = null;
        permissionMessage = null;
        requestPermission = null;
        openSettings = null;
        scanInstruction = null;
        super.onDestroyView();
    }

    private void renderPermissionState() {
        if (permissionPanel == null) {
            return;
        }
        CameraPermissionState permissionState = CameraPermissionState.resolve(
                hasCameraPermission(),
                cameraRequested,
                shouldShowRequestPermissionRationale(Manifest.permission.CAMERA));
        switch (permissionState) {
            case GRANTED:
                permissionPanel.setVisibility(View.GONE);
                openSettings.setVisibility(View.GONE);
                showCamera();
                break;
            case DENIED_CAN_ASK:
                stopCamera();
                permissionPanel.setVisibility(View.VISIBLE);
                permissionMessage.setText(R.string.camera_permission_denied);
                requestPermission.setVisibility(View.VISIBLE);
                openSettings.setVisibility(View.GONE);
                break;
            case PERMANENTLY_DENIED:
                stopCamera();
                permissionPanel.setVisibility(View.VISIBLE);
                permissionMessage.setText(R.string.camera_permission_permanently_denied);
                requestPermission.setVisibility(View.GONE);
                openSettings.setVisibility(View.VISIBLE);
                break;
            default:
                stopCamera();
                permissionPanel.setVisibility(View.VISIBLE);
                permissionMessage.setText(R.string.camera_permission_explanation);
                requestPermission.setVisibility(View.VISIBLE);
                openSettings.setVisibility(View.GONE);
                break;
        }
    }

    private void requestCameraPermission() {
        if (shouldShowRequestPermissionRationale(Manifest.permission.CAMERA)) {
            new AlertDialog.Builder(requireContext())
                    .setTitle(R.string.camera_permission_title)
                    .setMessage(R.string.camera_permission_explanation)
                    .setNegativeButton(android.R.string.cancel, null)
                    .setPositiveButton(R.string.continue_action,
                            (dialog, which) -> launchCameraPermission())
                    .show();
            return;
        }
        launchCameraPermission();
    }

    private void launchCameraPermission() {
        cameraRequested = true;
        cameraPermissionLauncher.launch(Manifest.permission.CAMERA);
    }

    private void openApplicationSettings() {
        Intent intent = new Intent(
                Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
                Uri.fromParts("package", requireContext().getPackageName(), null));
        startActivity(intent);
    }

    private boolean hasCameraPermission() {
        return ContextCompat.checkSelfPermission(requireContext(), Manifest.permission.CAMERA)
                == PackageManager.PERMISSION_GRANTED;
    }

    private void showCamera() {
        if (previewView == null || scanCaptured.get()) {
            return;
        }
        permissionPanel.setVisibility(View.GONE);
        stateView.hide();
        previewView.setVisibility(View.VISIBLE);
        scanInstruction.setVisibility(View.VISIBLE);
        cameraWanted = true;
        startCamera();
    }

    private void startCamera() {
        if (!hasCameraPermission() || cameraProvider != null || cameraStartPending
                || analysisExecutor == null) {
            return;
        }
        cameraStartPending = true;
        ListenableFuture<ProcessCameraProvider> providerFuture =
                ProcessCameraProvider.getInstance(requireContext());
        providerFuture.addListener(() -> {
            cameraStartPending = false;
            if (!isAdded() || previewView == null || scanCaptured.get() || !cameraWanted) {
                return;
            }
            try {
                cameraProvider = providerFuture.get();
                Preview preview = new Preview.Builder().build();
                preview.setSurfaceProvider(previewView.getSurfaceProvider());
                ImageAnalysis analysis = new ImageAnalysis.Builder()
                        .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                        .build();
                analysis.setAnalyzer(analysisExecutor, this::analyzeFrame);
                cameraProvider.unbindAll();
                cameraProvider.bindToLifecycle(
                        getViewLifecycleOwner(),
                        CameraSelector.DEFAULT_BACK_CAMERA,
                        preview,
                        analysis);
            } catch (Exception exception) {
                cameraProvider = null;
                if (stateView != null) {
                    stateView.showError(new ApiError(
                            ApiError.Kind.UNKNOWN,
                            0,
                            getString(R.string.camera_start_failed)),
                            ignored -> startCamera());
                }
            }
        }, ContextCompat.getMainExecutor(requireContext()));
    }

    @ExperimentalGetImage
    private void analyzeFrame(ImageProxy imageProxy) {
        if (!frameInFlight.compareAndSet(false, true) || scanCaptured.get()) {
            imageProxy.close();
            return;
        }
        android.media.Image mediaImage = imageProxy.getImage();
        if (mediaImage == null || barcodeScanner == null) {
            frameInFlight.set(false);
            imageProxy.close();
            return;
        }
        InputImage image = InputImage.fromMediaImage(
                mediaImage,
                imageProxy.getImageInfo().getRotationDegrees());
        barcodeScanner.process(image)
                .addOnSuccessListener(barcodes -> {
                    for (Barcode barcode : barcodes) {
                        String rawValue = barcode.getRawValue();
                        if (rawValue != null && !rawValue.trim().isEmpty()
                                && scanCaptured.compareAndSet(false, true)) {
                            if (!isAdded()) {
                                return;
                            }
                            requireActivity().runOnUiThread(() -> {
                                if (!isAdded() || viewModel == null) {
                                    return;
                                }
                                stopCamera();
                                viewModel.verifyScannedToken(rawValue);
                            });
                            break;
                        }
                    }
                })
                .addOnCompleteListener(ignored -> {
                    frameInFlight.set(false);
                    imageProxy.close();
                });
    }

    private void stopCamera() {
        cameraWanted = false;
        if (cameraProvider != null) {
            cameraProvider.unbindAll();
            cameraProvider = null;
        }
        if (previewView != null) {
            previewView.setVisibility(View.GONE);
        }
        if (scanInstruction != null) {
            scanInstruction.setVisibility(View.GONE);
        }
    }

    private void navigateToVerification(View view) {
        NavController controller = Navigation.findNavController(view);
        if (controller.getCurrentDestination() != null
                && controller.getCurrentDestination().getId() == R.id.nav_qr_operations) {
            controller.navigate(R.id.nav_transaction_verification);
        }
    }
}
