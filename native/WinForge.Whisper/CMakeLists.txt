cmake_minimum_required(VERSION 3.20)
project(WinForgeWhisper LANGUAGES C CXX)
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(WHISPER_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(WHISPER_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(WHISPER_BUILD_SERVER OFF CACHE BOOL "" FORCE)
set(GGML_BACKEND_DL OFF CACHE BOOL "" FORCE)
add_subdirectory("${WHISPER_SOURCE}" whisper)

# whisper.cpp v1.8.3 checks cancellation only after an entire scheduler graph.
# Wire its existing CPU backend abort callback into encoder/decoder graphs, without
# modifying the checked-out upstream tree. Fail configuration if upstream changes.
set(_whisper_source "${WHISPER_SOURCE}/src/whisper.cpp")
set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${_whisper_source}")
file(READ "${_whisper_source}" _whisper_text)
set(_old_signature [=[                      bool   sched_reset = true) {]=])
set(_new_signature [=[                      bool   sched_reset = true,
         ggml_abort_callback   abort_callback = nullptr,
                        void * abort_callback_data = nullptr) {]=])
string(FIND "${_whisper_text}" "${_old_signature}" _signature_pos)
if(_signature_pos EQUAL -1)
    message(FATAL_ERROR "Unexpected whisper scheduler signature; cancellation patch needs review")
endif()
string(REPLACE "${_old_signature}" "${_new_signature}" _whisper_text "${_whisper_text}")
set(_old_compute [=[    const bool t = (ggml_backend_sched_graph_compute(sched, graph) == GGML_STATUS_SUCCESS);]=])
set(_new_compute [=[    for (int i = 0; i < ggml_backend_sched_get_n_backends(sched); ++i) {
        ggml_backend_t backend = ggml_backend_sched_get_backend(sched, i);
        ggml_backend_dev_t dev = ggml_backend_get_device(backend);
        ggml_backend_reg_t reg = dev ? ggml_backend_dev_backend_reg(dev) : nullptr;
        auto * set_abort = reg ? (ggml_backend_set_abort_callback_t) ggml_backend_reg_get_proc_address(reg, "ggml_backend_set_abort_callback") : nullptr;
        if (set_abort) set_abort(backend, abort_callback, abort_callback_data);
    }
    const bool t = (ggml_backend_sched_graph_compute(sched, graph) == GGML_STATUS_SUCCESS);
    // Backends outlive the inference callback and must never retain its pointer.
    for (int i = 0; i < ggml_backend_sched_get_n_backends(sched); ++i) {
        ggml_backend_t backend = ggml_backend_sched_get_backend(sched, i);
        ggml_backend_dev_t dev = ggml_backend_get_device(backend);
        ggml_backend_reg_t reg = dev ? ggml_backend_dev_backend_reg(dev) : nullptr;
        auto * set_abort = reg ? (ggml_backend_set_abort_callback_t) ggml_backend_reg_get_proc_address(reg, "ggml_backend_set_abort_callback") : nullptr;
        if (set_abort) set_abort(backend, nullptr, nullptr);
    }]=])
string(FIND "${_whisper_text}" "${_old_compute}" _compute_pos)
if(_compute_pos EQUAL -1)
    message(FATAL_ERROR "Unexpected whisper scheduler implementation; cancellation patch needs review")
endif()
string(REPLACE "${_old_compute}" "${_new_compute}" _whisper_text "${_whisper_text}")
set(_old_call "ggml_graph_compute_helper(sched, gf, n_threads)")
string(REGEX MATCHALL "ggml_graph_compute_helper\\(sched, gf, n_threads\\)" _calls "${_whisper_text}")
list(LENGTH _calls _call_count)
if(NOT _call_count EQUAL 4)
    message(FATAL_ERROR "Unexpected whisper encoder/decoder calls; cancellation patch needs review")
endif()
string(REPLACE "${_old_call}" "ggml_graph_compute_helper(sched, gf, n_threads, true, abort_callback, abort_callback_data)" _whisper_text "${_whisper_text}")
set(_generated_whisper "${CMAKE_CURRENT_BINARY_DIR}/whisper-cancellable.cpp")
file(WRITE "${_generated_whisper}" "${_whisper_text}")
set_property(TARGET whisper PROPERTY SOURCES "${WHISPER_SOURCE}/include/whisper.h" "${WHISPER_SOURCE}/src/whisper-arch.h" "${_generated_whisper}")

add_library(WinForge.Whisper SHARED WhisperBridge.cpp)
target_link_libraries(WinForge.Whisper PRIVATE whisper)
target_compile_features(WinForge.Whisper PRIVATE cxx_std_17)
