; Bron van de patch 'game-foreground' (Patching/GameForeground.cs); niet meegebouwd, alleen ter referentie.
; Bouwen: ml /c /coff GameForeground.asm; de bytes van .text staan in GameForeground.Code.
; De string-adressen (S_*) volgen uit GameForeground.Strings, vanaf 0x405B20.
; Vervangt WaitForSingleObject(hProcess, INFINITE) in _spawn (P_WAIT) van RedCat.exe.
; stdcall(hHandle, dwMilliseconds), geeft het resultaat van WaitForSingleObject terug.
.386
.model flat
S_user32_dll EQU 0405B20h
S_kernel32_dll EQU 0405B2Bh
S_RedCat EQU 0405B38h
S_FindWindowA EQU 0405B3Fh
S_GetForegroundWindow EQU 0405B4Bh
S_SetForegroundWindow EQU 0405B5Fh
S_IsWindowVisible EQU 0405B73h
S_GetWindowThreadProcessId EQU 0405B83h
S_AttachThreadInput EQU 0405B9Ch
S_GetCurrentThreadId EQU 0405BAEh
S_keybd_event EQU 0405BC1h
GetModuleHandleA    EQU 406060h
GetProcAddress      EQU 406050h
WaitForSingleObject EQU 406014h
PeekMessageA        EQU 406110h
TranslateMessage    EQU 406108h
DispatchMessageA    EQU 4060D4h
PostQuitMessage     EQU 4060DCh

pFind   EQU -24h
pGetFg  EQU -28h
pSetFg  EQU -2Ch
pVis    EQU -30h
pThread EQU -34h
pAttach EQU -38h
pCurTid EQU -3Ch
tries   EQU -40h
done    EQU -44h
pKey    EQU -48h
msg     EQU -20h

RESOLVE MACRO nameaddr, slot
    LOCAL ok
    push nameaddr
    push ebx
    call dword ptr ds:[GetProcAddress]
    mov [ebp+slot], eax
    test eax, eax
    jnz ok
    mov dword ptr [ebp+done], 1
ok:
ENDM

.code
stub:
    push ebp
    mov ebp, esp
    sub esp, 4Ch
    push ebx
    push esi
    push edi
    mov dword ptr [ebp+tries], 40       ; 40 x 50 ms proberen zodra het spelvenster zichtbaar is
    mov dword ptr [ebp+done], 0
    push S_user32_dll
    call dword ptr ds:[GetModuleHandleA]
    mov ebx, eax
    RESOLVE S_FindWindowA, pFind
    RESOLVE S_GetForegroundWindow, pGetFg
    RESOLVE S_SetForegroundWindow, pSetFg
    RESOLVE S_IsWindowVisible, pVis
    RESOLVE S_GetWindowThreadProcessId, pThread
    RESOLVE S_AttachThreadInput, pAttach
    RESOLVE S_keybd_event, pKey
    push S_kernel32_dll
    call dword ptr ds:[GetModuleHandleA]
    mov ebx, eax
    RESOLVE S_GetCurrentThreadId, pCurTid

wait_loop:
    push 50
    push dword ptr [ebp+8]
    call dword ptr ds:[WaitForSingleObject]
    cmp eax, 102h                       ; WAIT_TIMEOUT
    jne finish
pump:                                   ; berichten van het MOTOR-venster verwerken
    push 1                              ; PM_REMOVE
    push 0
    push 0
    push 0
    lea eax, [ebp+msg]
    push eax
    call dword ptr ds:[PeekMessageA]
    test eax, eax
    jz pumped
    cmp dword ptr [ebp+msg+4], 12h      ; WM_QUIT: opnieuw posten voor de hoofdlus
    jne dispatch
    push dword ptr [ebp+msg+8]
    call dword ptr ds:[PostQuitMessage]
    jmp pumped
dispatch:
    lea eax, [ebp+msg]
    push eax
    call dword ptr ds:[TranslateMessage]
    lea eax, [ebp+msg]
    push eax
    call dword ptr ds:[DispatchMessageA]
    jmp pump

pumped:                                 ; spelvenster (klasse "RedCat") naar voren halen
    cmp dword ptr [ebp+done], 0
    jne wait_loop
    push 0
    push S_RedCat
    call dword ptr [ebp+pFind]
    test eax, eax
    jz wait_loop
    mov esi, eax
    push esi
    call dword ptr [ebp+pVis]
    test eax, eax
    jz wait_loop
    call dword ptr [ebp+pGetFg]
    cmp eax, esi
    je mark_done
    dec dword ptr [ebp+tries]
    js mark_done
    cmp dword ptr [ebp+tries], 30       ; na 10 mislukte pogingen eenmalig Alt indrukken en loslaten:
    jne no_alt                          ; toetsenbordinvoer geeft dit proces het voorgrondrecht
    push eax
    push 0
    push 0
    push 0
    push 12h                            ; VK_MENU
    call dword ptr [ebp+pKey]
    push 0
    push 2                              ; KEYEVENTF_KEYUP
    push 0
    push 12h
    call dword ptr [ebp+pKey]
    pop eax
no_alt:
    push 0
    push eax
    call dword ptr [ebp+pThread]        ; thread van het huidige voorgrondvenster
    mov edi, eax
    call dword ptr [ebp+pCurTid]
    mov ebx, eax
    test edi, edi
    jz no_attach
    cmp edi, ebx
    je no_attach
    push 1
    push edi
    push ebx
    call dword ptr [ebp+pAttach]        ; AttachThreadInput(eigen, voorgrond, TRUE)
no_attach:
    push esi
    call dword ptr [ebp+pSetFg]
    test edi, edi
    jz wait_loop
    cmp edi, ebx
    je wait_loop
    push 0
    push edi
    push ebx
    call dword ptr [ebp+pAttach]        ; en weer los
    jmp wait_loop
mark_done:
    mov dword ptr [ebp+done], 1
    jmp wait_loop

finish:
    pop edi
    pop esi
    pop ebx
    leave
    ret 8
END
