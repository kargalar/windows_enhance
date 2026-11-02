#pragma once
#include <windows.h>
#include <algorithm>
#include <string>
#include <vector>
#include <map>

namespace WinForgeMenu {
inline std::wstring Normalize(std::wstring text) {
    auto tab = text.find(L'\t'); if (tab != std::wstring::npos) text.resize(tab);
    text.erase(std::remove(text.begin(), text.end(), L'&'), text.end());
    for (auto suffix : {std::wstring(L"..."), std::wstring(L"\x2026")}) {
        auto pos = text.find(suffix); if (pos != std::wstring::npos) text.erase(pos, suffix.size());
    }
    auto first = text.find_first_not_of(L" \t\r\n"), last = text.find_last_not_of(L" \t\r\n");
    if (first == std::wstring::npos) return {};
    text = text.substr(first, last - first + 1);
    std::wstring result(text.size(), L'\0');
    LCMapStringEx(LOCALE_NAME_INVARIANT, LCMAP_LOWERCASE, text.data(), static_cast<int>(text.size()), result.data(), static_cast<int>(result.size()), nullptr, nullptr, 0);
    return result;
}
inline std::wstring Canonical(std::wstring const& text, std::wstring const& parent = {}) {
    auto name = Normalize(text), scope = Normalize(parent);
    if (name == L"masa\u00fcst\u00fc k\u0131sayolu olu\u015ftur") return L"prepared:desktop-shortcut";
    if (name == L"dosyay\u0131 / k\u0131sayolu gizle veya g\u00f6ster") return L"prepared:toggle-hidden";
    if (name == L"klas\u00f6r simgesini de\u011fi\u015ftir") return L"prepared:folder-icon";
    if (name == L"gizli \u00f6\u011feleri g\u00f6ster / gizle") return L"prepared:hidden-items";
    if (name == L"g\u00fc\u00e7 modu") return L"prepared:power-mode";
    if (name == L"properties" || name == L"\x00f6zellikler" || name == L"ozellikler") return L"builtin:properties";
    if (name == L"new folder" || name == L"yeni klas\x00f6r" || ((scope == L"new" || scope == L"yeni") && (name == L"folder" || name == L"klas\x00f6r"))) return L"builtin:new-folder";
    if (name == L"new" || name == L"yeni") return L"builtin:new";
    if (name == L"copy" || name == L"kopyala") return L"builtin:copy";
    if (name == L"copy as path" || name == L"yol olarak kopyala") return L"builtin:copy-path";
    if (name == L"cut" || name == L"kes") return L"builtin:cut";
    if (name == L"paste" || name == L"yap\x0131\x015ft\x0131r") return L"builtin:paste";
    if (name == L"delete" || name == L"sil") return L"builtin:delete";
    if (name == L"rename" || name == L"yeniden adland\x0131r") return L"builtin:rename";
    if (name == L"refresh" || name == L"yenile") return L"builtin:refresh";
    return {};
}
struct Rule { std::wstring match, name, group, icon; bool visible{true}; unsigned contexts{255}; std::wstring groupIcon; bool separatorBefore{}, separatorAfter{}; };
struct Options { bool cleanSeparators{true}, showSeparators{true}, unquotedCopyPath{true}; };
inline unsigned SelectionScope(std::wstring const& path, bool folder, bool link, bool filesystem) {
    if (!filesystem) return 128u;
    if (path.size() == 3 && path[1] == L':' && path[2] == L'\\') return 16u;
    auto dot = path.find_last_of(L'.');
    auto extension = dot == std::wstring::npos ? L"" : Normalize(path.substr(dot));
    if (link) return 32u;
    if (folder) return 2u;
    if (extension == L".lnk" || extension == L".url") return 32u;
    if (extension == L".exe" || extension == L".com" || extension == L".bat" || extension == L".cmd" || extension == L".msi" || extension == L".ps1") return 64u;
    return 1u;
}
struct Item {
    HMENU menu{}; UINT position{}; MENUITEMINFOW info{}; std::wstring text, parent;
    MENUITEMINFOW Data() const { auto copy = info; copy.dwTypeData = const_cast<wchar_t*>(text.c_str()); copy.cch = static_cast<UINT>(text.size()); return copy; }
};
inline std::vector<Item> ReadItems(HWND owner, HMENU menu, bool populate, std::wstring const& parent = {}, unsigned depth = 0) {
    std::vector<Item> result; if (depth > 4 || !IsMenu(menu)) return result;
    auto count = GetMenuItemCount(menu);
    for (int i = 0; i < count; ++i) {
        wchar_t label[2048]{}; Item item; item.menu = menu; item.position = static_cast<UINT>(i); item.parent = parent;
        item.info.cbSize = sizeof(item.info);
        item.info.fMask = MIIM_FTYPE | MIIM_STATE | MIIM_ID | MIIM_SUBMENU | MIIM_CHECKMARKS | MIIM_DATA | MIIM_BITMAP | MIIM_STRING;
        item.info.dwTypeData = label; item.info.cch = static_cast<UINT>(std::size(label));
        if (!GetMenuItemInfoW(menu, static_cast<UINT>(i), TRUE, &item.info)) continue;
        item.text = label; item.info.dwTypeData = nullptr; result.push_back(item);
        if (item.info.hSubMenu) {
            // Populate our power cascade before grouping changes its parent/position.
            // Explorer resolves lazy cascades using the original menu location.
            // New also needs early initialization to promote its Folder command.
            // Opening unrelated extension submenus can be expensive or contact remote locations.
            auto canonical = Canonical(item.text);
            if (populate && owner && (canonical == L"builtin:new" || canonical == L"prepared:power-mode")) SendMessageW(owner, WM_INITMENUPOPUP, reinterpret_cast<WPARAM>(item.info.hSubMenu), MAKELPARAM(i, FALSE));
            auto children = ReadItems(owner, item.info.hSubMenu, populate, item.text, depth + 1);
            result.insert(result.end(), children.begin(), children.end());
        }
    }
    return result;
}
struct Changed { Item original; bool removed{}; };
inline std::wstring Identity(Item const& item) { auto canonical = Canonical(item.text, item.parent); return canonical.empty() ? Normalize(item.text) : canonical; }
inline std::wstring SeparatorIdentity(std::vector<Item> const& items, Item const& separator) {
    std::wstring previous, next;
    for (auto const& item : items) {
        if (item.menu != separator.menu || (item.info.fType & MFT_SEPARATOR)) continue;
        if (item.position < separator.position) previous = Identity(item);
        else if (item.position > separator.position) { next = Identity(item); break; }
    }
    auto text = Normalize(separator.parent) + L"|" + previous + L"|" + next;
    unsigned long long hash = 14695981039346656037ull;
    for (auto character : text) { hash ^= static_cast<unsigned short>(character); hash *= 1099511628211ull; }
    wchar_t id[40]{}; swprintf_s(id, L"separator:%016llX", hash); return id;
}
// Match the exact quoted selection produced by Copy as path. Other clipboard
// contents, including quoted prose and unrelated file paths, are left alone.
inline std::wstring PlainCopiedPaths(std::wstring const& text, std::vector<std::wstring> const& paths) {
    if (paths.empty()) return {};
    std::wstring quoted, plain;
    for (auto const& path : paths) {
        if (path.empty()) return {};
        if (!quoted.empty()) { quoted += L"\r\n"; plain += L"\r\n"; }
        quoted += L"\"" + path + L"\""; plain += path;
    }
    return text == quoted || text == quoted + L"\r\n" ? plain : L"";
}
inline bool DarkMenus() {
    HIGHCONTRASTW contrast{sizeof(contrast)};
    if (SystemParametersInfoW(SPI_GETHIGHCONTRAST, sizeof(contrast), &contrast, 0) && (contrast.dwFlags & HCF_HIGHCONTRASTON)) return false;
    DWORD light = 1, bytes = sizeof(light);
    RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", L"AppsUseLightTheme", RRF_RT_REG_DWORD, nullptr, &light, &bytes);
    return light == 0;
}
inline COLORREF GroupTextColor(bool dark, bool selected) { return dark ? RGB(255, 255, 255) : GetSysColor(selected ? COLOR_HIGHLIGHTTEXT : COLOR_MENUTEXT); }
class Transaction {
    HMENU root{};
    std::vector<Changed> changes;
    std::map<std::wstring, HMENU> groups;
    struct GroupVisual { std::wstring title; HBITMAP bitmap{}; };
    std::map<HMENU, GroupVisual> visuals;
    std::vector<HBITMAP> bitmaps;
    std::vector<HMENU> menus;
    std::vector<HMENU> separatorMenus;
    struct Anchor { HMENU menu; Item item; bool before, after; };
    std::vector<Anchor> anchors;
public:
    ~Transaction() { Restore(); }
    bool IsGroup(HMENU menu) const { if (std::find(menus.begin(), menus.end(), menu) != menus.end()) return true; for (auto const& entry : groups) if (entry.second == menu) return true; return false; }
    // Only handle our own item data; extension-owned items keep their original drawing.
    bool HandleGroupMessage(HWND owner, UINT message, LPARAM parameter) const {
        if (!parameter || (message != WM_MEASUREITEM && message != WM_DRAWITEM)) return false;
        auto measure = message == WM_MEASUREITEM ? reinterpret_cast<MEASUREITEMSTRUCT*>(parameter) : nullptr;
        auto draw = message == WM_DRAWITEM ? reinterpret_cast<DRAWITEMSTRUCT*>(parameter) : nullptr;
        if ((measure ? measure->CtlType : draw->CtlType) != ODT_MENU) return false;
        auto data = measure ? measure->itemData : draw->itemData;
        auto found = std::find_if(visuals.begin(), visuals.end(), [&](auto const& pair) { return reinterpret_cast<ULONG_PTR>(&pair.second) == data; });
        if (found == visuals.end()) return false;
        auto const& visual = found->second;
        auto dpi = GetDpiForWindow(owner); if (!dpi) dpi = 96;
        auto scale = [dpi](int value) { return MulDiv(value, static_cast<int>(dpi), 96); };
        NONCLIENTMETRICSW metrics{}; metrics.cbSize = sizeof(metrics);
        SystemParametersInfoForDpi(SPI_GETNONCLIENTMETRICS, sizeof(metrics), &metrics, 0, dpi);
        auto font = CreateFontIndirectW(&metrics.lfMenuFont);
        auto dc = measure ? GetDC(owner) : draw->hDC;
        auto saved = SaveDC(dc); if (font) SelectObject(dc, font);
        if (measure) {
            SIZE size{}; GetTextExtentPoint32W(dc, visual.title.c_str(), static_cast<int>(visual.title.size()), &size);
            measure->itemWidth = static_cast<UINT>(size.cx + scale(56));
            measure->itemHeight = static_cast<UINT>(std::max<LONG>(size.cy + scale(8), scale(28)));
        } else {
            bool selected = (draw->itemState & ODS_SELECTED) != 0, dark = DarkMenus();
            auto background = dark ? (selected ? RGB(65, 65, 65) : RGB(43, 43, 43)) : GetSysColor(selected ? COLOR_HIGHLIGHT : COLOR_MENU);
            auto brush = CreateSolidBrush(background); FillRect(dc, &draw->rcItem, brush); DeleteObject(brush);
            SetBkMode(dc, TRANSPARENT); SetTextColor(dc, GroupTextColor(dark, selected));
            auto rect = draw->rcItem; rect.left += scale(32); rect.right -= scale(20);
            DrawTextW(dc, visual.title.c_str(), static_cast<int>(visual.title.size()), &rect, DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
            if (visual.bitmap) {
                BITMAP bitmap{}; GetObjectW(visual.bitmap, sizeof(bitmap), &bitmap);
                auto memory = CreateCompatibleDC(dc); auto original = SelectObject(memory, visual.bitmap);
                BLENDFUNCTION blend{AC_SRC_OVER, 0, 255, AC_SRC_ALPHA};
                AlphaBlend(dc, draw->rcItem.left + scale(6), draw->rcItem.top + (draw->rcItem.bottom - draw->rcItem.top - scale(20)) / 2,
                    scale(20), scale(20), memory, 0, 0, bitmap.bmWidth, bitmap.bmHeight, blend);
                SelectObject(memory, original); DeleteDC(memory);
            }
        }
        RestoreDC(dc, saved); if (measure) ReleaseDC(owner, dc); if (font) DeleteObject(font);
        return true;
    }
    bool IsCopyPath(UINT command) const { return std::find(copyPathCommands.begin(), copyPathCommands.end(), command) != copyPathCommands.end(); }
    std::vector<UINT> copyPathCommands;
    void Apply(HMENU menu, std::vector<Item> const& items, std::vector<Rule> const& rules, unsigned context = 0, Options options = {}) {
        root = menu;
        for (auto const& item : items) if (Canonical(item.text, item.parent) == L"builtin:copy-path") copyPathCommands.push_back(item.info.wID);
        for (auto const& item : items) { menus.push_back(item.menu); if (item.info.hSubMenu) menus.push_back(item.info.hSubMenu); }
        for (auto cursor = items.rbegin(); cursor != items.rend(); ++cursor) {
            auto const& item = *cursor;
            bool separator = (item.info.fType & MFT_SEPARATOR) != 0;
            if (item.text.empty() && !separator) continue;
            auto canonical = separator ? SeparatorIdentity(items, item) : Canonical(item.text, item.parent), normalized = Normalize(item.text);
            auto found = std::find_if(rules.begin(), rules.end(), [&](auto const& rule) { return rule.match == normalized || (!canonical.empty() && rule.match == canonical); });
            if (found == rules.end()) continue;
            auto data = item.Data();
            bool visible = context ? (found->contexts & context) == context : found->visible;
            bool remove = !visible || (!separator && !found->group.empty());
            if (remove && !RemoveMenu(item.menu, item.position, MF_BYPOSITION)) continue;
            changes.push_back({item, remove});
            if (!visible || separator) continue;
            auto title = found->name.empty() ? item.text : found->name;
            data.dwTypeData = title.data(); data.cch = static_cast<UINT>(title.size());
            if (!found->icon.empty()) {
                auto bitmap = static_cast<HBITMAP>(LoadImageW(nullptr, found->icon.c_str(), IMAGE_BITMAP, 0, 0, LR_LOADFROMFILE | LR_CREATEDIBSECTION));
                if (bitmap) { data.hbmpItem = bitmap; bitmaps.push_back(bitmap); }
            }
            if (!found->group.empty()) {
                auto& group = groups[found->group];
                if (!group) {
                    group = CreatePopupMenu();
                    auto& visual = visuals[group]; visual.title = found->group;
                    if (!found->groupIcon.empty()) {
                        visual.bitmap = static_cast<HBITMAP>(LoadImageW(nullptr, found->groupIcon.c_str(), IMAGE_BITMAP, 0, 0, LR_LOADFROMFILE | LR_CREATEDIBSECTION));
                        if (visual.bitmap) {
                            bitmaps.push_back(visual.bitmap);
                            DIBSECTION dib{};
                            if (GetObjectW(visual.bitmap, sizeof(dib), &dib) == sizeof(dib) && dib.dsBm.bmBitsPixel == 32 && dib.dsBm.bmBits) {
                                auto pixels = static_cast<BYTE*>(dib.dsBm.bmBits);
                                auto count = static_cast<size_t>(dib.dsBm.bmWidthBytes) * dib.dsBm.bmHeight;
                                bool alpha = false; for (size_t i = 3; i < count; i += 4) if (pixels[i]) { alpha = true; break; }
                                for (size_t i = 0; i < count; i += 4) {
                                    if (!alpha) pixels[i + 3] = 255;
                                    for (size_t c = 0; c < 3; ++c) pixels[i + c] = static_cast<BYTE>(pixels[i + c] * pixels[i + 3] / 255);
                                }
                            }
                        }
                    }
                    MENUITEMINFOW header{}; header.cbSize = sizeof(header);
                    header.fMask = MIIM_FTYPE | MIIM_SUBMENU | MIIM_STRING | MIIM_DATA;
                    header.fType = MFT_OWNERDRAW; header.hSubMenu = group;
                    header.dwTypeData = visual.title.data(); header.dwItemData = reinterpret_cast<ULONG_PTR>(&visual);
                    InsertMenuItemW(root, static_cast<UINT>(GetMenuItemCount(root)), TRUE, &header);
                }
                // Reverse traversal preserves original positions while removing; insert at front to retain order.
                if (!InsertMenuItemW(group, 0, TRUE, &data)) { auto original = item.Data(); InsertMenuItemW(item.menu, item.position, TRUE, &original); changes.pop_back(); }
                else anchors.push_back({group, item, found->separatorBefore, found->separatorAfter});
            } else { SetMenuItemInfoW(item.menu, item.position, TRUE, &data); anchors.push_back({item.menu, item, found->separatorBefore, found->separatorAfter}); }
        }
        CleanSeparators(items, options);
        for (auto const& anchor : anchors) AddSeparators(anchor);
    }
private:
    void CleanSeparators(std::vector<Item> const& originals, Options options) {
        std::map<HMENU, std::vector<Item>> survivors;
        for (auto const& item : originals) {
            bool removed = std::any_of(changes.begin(), changes.end(), [&](auto const& change) { return change.removed && change.original.menu == item.menu && change.original.position == item.position; });
            if (!removed) survivors[item.menu].push_back(item);
        }
        for (auto const& [menu, items] : survivors) {
            std::vector<UINT> remove;
            bool content = false, lastSeparator = false;
            auto count = GetMenuItemCount(menu);
            for (int i = 0; i < count; ++i) {
                bool separator = (GetMenuState(menu, static_cast<UINT>(i), MF_BYPOSITION) & MF_SEPARATOR) != 0;
                if (separator && (!options.showSeparators || (options.cleanSeparators && (!content || lastSeparator || i == count - 1)))) remove.push_back(static_cast<UINT>(i));
                else { lastSeparator = separator; if (!separator) content = true; }
            }
            // A run at the end can leave its first separator after later ones were marked.
            if (options.cleanSeparators && lastSeparator) {
                for (int i = count - 1; i >= 0 && (GetMenuState(menu, static_cast<UINT>(i), MF_BYPOSITION) & MF_SEPARATOR); --i)
                    if (std::find(remove.begin(), remove.end(), static_cast<UINT>(i)) == remove.end()) remove.push_back(static_cast<UINT>(i));
            }
            std::sort(remove.rbegin(), remove.rend());
            for (auto position : remove) if (position < items.size() && RemoveMenu(menu, position, MF_BYPOSITION)) changes.push_back({items[position], true});
        }
    }
    void AddSeparators(Anchor const& anchor) {
        if (!anchor.before && !anchor.after) return;
        auto count = GetMenuItemCount(anchor.menu);
        for (int i = 0; i < count; ++i) {
            MENUITEMINFOW item{}; item.cbSize = sizeof(item); item.fMask = MIIM_ID | MIIM_SUBMENU | MIIM_DATA | MIIM_FTYPE;
            if (!GetMenuItemInfoW(anchor.menu, static_cast<UINT>(i), TRUE, &item) || (item.fType & MFT_SEPARATOR) || item.wID != anchor.item.info.wID || item.hSubMenu != anchor.item.info.hSubMenu || item.dwItemData != anchor.item.info.dwItemData) continue;
            auto insert = [&](UINT position) {
                if ((position > 0 && (GetMenuState(anchor.menu, position - 1, MF_BYPOSITION) & MF_SEPARATOR)) ||
                    (position < static_cast<UINT>(GetMenuItemCount(anchor.menu)) && (GetMenuState(anchor.menu, position, MF_BYPOSITION) & MF_SEPARATOR))) return;
                MENUITEMINFOW separator{}; separator.cbSize = sizeof(separator); separator.fMask = MIIM_FTYPE | MIIM_DATA;
                separator.fType = MFT_SEPARATOR; separator.dwItemData = reinterpret_cast<ULONG_PTR>(this);
                if (InsertMenuItemW(anchor.menu, position, TRUE, &separator)) separatorMenus.push_back(anchor.menu);
            };
            if (anchor.after) insert(static_cast<UINT>(i + 1));
            if (anchor.before) insert(static_cast<UINT>(i));
            break;
        }
    }
public:
    void Restore() {
        for (auto menu : separatorMenus) if (IsMenu(menu)) for (int i = GetMenuItemCount(menu) - 1; i >= 0; --i) {
            MENUITEMINFOW item{}; item.cbSize = sizeof(item); item.fMask = MIIM_FTYPE | MIIM_DATA;
            if (GetMenuItemInfoW(menu, static_cast<UINT>(i), TRUE, &item) && (item.fType & MFT_SEPARATOR) && item.dwItemData == reinterpret_cast<ULONG_PTR>(this)) RemoveMenu(menu, static_cast<UINT>(i), MF_BYPOSITION);
        }
        for (auto const& entry : groups) {
            auto group = entry.second;
            if (IsMenu(group)) while (GetMenuItemCount(group) > 0) RemoveMenu(group, 0, MF_BYPOSITION);
            if (IsMenu(root)) for (int i = GetMenuItemCount(root) - 1; i >= 0; --i) if (GetSubMenu(root, i) == group) RemoveMenu(root, static_cast<UINT>(i), MF_BYPOSITION);
            if (IsMenu(group)) DestroyMenu(group);
        }
        std::sort(changes.begin(), changes.end(), [](auto const& a, auto const& b) { if (a.original.menu != b.original.menu) return reinterpret_cast<UINT_PTR>(a.original.menu) < reinterpret_cast<UINT_PTR>(b.original.menu); return a.original.position < b.original.position; });
        for (auto const& change : changes) if (change.removed && IsMenu(change.original.menu)) { auto original = change.original.Data(); InsertMenuItemW(change.original.menu, change.original.position, TRUE, &original); }
        for (auto const& change : changes) if (!change.removed && IsMenu(change.original.menu)) { auto original = change.original.Data(); SetMenuItemInfoW(change.original.menu, change.original.position, TRUE, &original); }
        for (auto bitmap : bitmaps) DeleteObject(bitmap);
        changes.clear(); groups.clear(); visuals.clear(); bitmaps.clear(); menus.clear(); separatorMenus.clear(); anchors.clear(); copyPathCommands.clear(); root = nullptr;
    }
};
}
