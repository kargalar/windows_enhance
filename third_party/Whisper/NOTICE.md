# Whisper engine and models

WinForge contains its own capture/dictation workflow and process protocol. It links a native
bridge against whisper.cpp v1.8.3, commit `2eeeba56e9edd762b4b38467bab96c2517163158`:
https://github.com/ggml-org/whisper.cpp

The upstream engine is MIT licensed; see LICENSE. Whisper model weights use OpenAI's MIT
license; see LICENSE-OpenAI.txt. Model checksums are maintained in WhisperModels.cs.

Optional CUDA packages include NVIDIA cuBLAS redistributables and NVIDIA-EULA.txt.
The CPU package does not need CUDA. FFmpeg licensing follows the existing verified media
tool installation and its bundled license notices. No ShareX code is incorporated.
