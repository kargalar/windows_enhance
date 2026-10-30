#include "whisper.h"
#include "ggml-backend.h"
#include <algorithm>
#include <cstring>
#include <string>
#include <thread>

extern "C" __declspec(dllexport) whisper_context* wf_load(const char* path, bool gpu) {
    auto parameters = whisper_context_default_params();
    parameters.use_gpu = gpu;
    auto context = whisper_init_from_file_with_params(path, parameters);
    if (gpu && !ggml_backend_dev_by_type(GGML_BACKEND_DEVICE_TYPE_GPU)) {
        if (context) whisper_free(context);
        return nullptr;
    }
    return context;
}
extern "C" __declspec(dllexport) void wf_free(whisper_context* context) { whisper_free(context); }
extern "C" __declspec(dllexport) int wf_transcribe(whisper_context* context, const float* audio, int count,
    const char* language, bool translate, char* output, int capacity) {
    try {
        auto parameters = whisper_full_default_params(WHISPER_SAMPLING_GREEDY);
        parameters.n_threads = std::max(1, std::min(8, static_cast<int>(std::thread::hardware_concurrency())));
        parameters.translate = translate;
        parameters.language = language;
        parameters.no_context = true;
        parameters.no_timestamps = true;
        parameters.print_progress = false;
        parameters.print_realtime = false;
        parameters.print_timestamps = false;
        if (whisper_full(context, parameters, audio, count) != 0) return -1;
        std::string text;
        for (int index = 0; index < whisper_full_n_segments(context); ++index)
            text += whisper_full_get_segment_text(context, index);
        if (text.size() >= static_cast<size_t>(capacity)) return -2;
        std::memcpy(output, text.data(), text.size());
        return static_cast<int>(text.size());
    } catch (...) { return -3; }
}
