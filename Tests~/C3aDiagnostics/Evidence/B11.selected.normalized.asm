; Assembly listing for method Program+<>c__DisplayClass10_0:<Run>b__10(int):this (FullOpts)
; Emitting BLENDED_CODE for X64 with AVX512 - Unix
; FullOpts code
; optimized code
; rbp based frame
; fully interruptible
; No PGO data
; 0 inlinees with PGO data; 24 single block inlinees; 8 inlinees without PGO data

G_M_IG01:                ;; offset=0x0000
       push     rbp
       push     r15
       push     r14
       push     r13
       push     r12
       push     rbx
       push     rax
       vzeroupper 
       lea      rbp, [rsp+0x30]
       mov      rbx, rdi
 
G_M_IG02:                ;; offset=0x0016
       mov      r15, gword ptr [rbx+0x08]
       mov      rdi, r15
       test     rdi, rdi
       je       G_M_IG13
 
G_M_IG03:                ;; offset=0x0026
       cmp      dword ptr [rdi+0x08], 9
       jne      G_M_IG13
 
G_M_IG04:                ;; offset=0x0030
       vmovups  xmm0, xmmword ptr [rdi+0x0C]
       vpxor    xmm0, xmm0, xmmword ptr [reloc @RWD00]
       vmovups  xmm1, xmmword ptr [rdi+0x0E]
       vpternlogq xmm0, xmm1, xmmword ptr [reloc @RWD16], -10
       vptest   xmm0, xmm0
       sete     dil
       movzx    rdi, dil
       test     edi, edi
       je       G_M_IG13
       mov      rdi, gword ptr [rbx+0x30]
       xor      esi, esi
       call     [Program:Build(GameplayTags.GameplayTag[],int):GameplayTags.GameplayTagContainer]
       mov      r14, rax
       mov      rdi, gword ptr [rbx+0x18]
       mov      esi, dword ptr [rbx+0x88]
       mov      eax, esi
       shr      eax, 31
       add      esi, eax
       sar      esi, 1
       cmp      esi, dword ptr [rdi+0x08]
       jae      G_M_IG45
       mov      r13, gword ptr [rdi+8*rsi+0x10]
       test     byte  ptr [(reloc 0xNATIVE_POINTER)], 1
       je       G_M_IG40
 
G_M_IG05:                ;; offset=0x009F
       mov      r12, 0xNATIVE_POINTER
       mov      r15, qword ptr [r12]
       cmp      byte  ptr [r14], r14b
       mov      rsi, r13
       mov      rdi, 0xNATIVE_POINTER
       test     rsi, rsi
       cmove    rsi, rdi
       mov      rdi, r14
       call     [GameplayTags.GameplayTagContainer:IndexOf(System.String):int:this]
       mov      edi, eax
       not      edi
       shr      edi, 31
       add      rdi, r15
       mov      qword ptr [r12], rdi
       mov      rdi, r14
       mov      rsi, r13
       call     [GameplayTags.GameplayTagContainer:RemoveTag(GameplayTags.GameplayTag):bool:this]
       mov      rdi, r14
       mov      rsi, r13
       call     [GameplayTags.GameplayTagContainer:AddTag(GameplayTags.GameplayTag):bool:this]
       xor      r13d, r13d
       mov      rsi, gword ptr [r14+0x08]
       mov      edi, dword ptr [rsi+0x10]
       test     edi, edi
       jle      SHORT G_M_IG09
 
G_M_IG06:                ;; offset=0x0101
       mov      eax, r13d
       mov      rcx, rsi
       cmp      eax, edi
       jae      G_M_IG43
       mov      rcx, gword ptr [rcx+0x08]
       cmp      eax, dword ptr [rcx+0x08]
       jae      G_M_IG45
       mov      rax, gword ptr [rcx+8*rax+0x10]
       mov      rcx, qword ptr [r12]
       test     rax, rax
       jne      SHORT G_M_IG08
 
G_M_IG07:                ;; offset=0x012A
       mov      rax, 0xNATIVE_POINTER
 
G_M_IG08:                ;; offset=0x0134
       mov      eax, dword ptr [rax+0x08]
       add      rax, rcx
       mov      qword ptr [r12], rax
       inc      r13d
       cmp      r13d, edi
       jl       SHORT G_M_IG06
 
G_M_IG09:                ;; offset=0x0146
       mov      rsi, gword ptr [rbx+0x28]
       mov      rdi, r14
       call     [GameplayTags.GameplayTagContainer:Union(GameplayTags.GameplayTagContainer,GameplayTags.GameplayTagContainer):GameplayTags.GameplayTagContainer]
       lea      rdi, bword ptr [rbx+0x38]
       mov      rsi, rax
       call     CORINFO_HELP_ASSIGN_REF
       mov      rsi, gword ptr [rbx+0x28]
       mov      rdi, r14
       call     [GameplayTags.GameplayTagContainer:IntersectionExact(GameplayTags.GameplayTagContainer,GameplayTags.GameplayTagContainer):GameplayTags.GameplayTagContainer]
       lea      rdi, bword ptr [rbx+0x40]
       mov      rsi, rax
       call     CORINFO_HELP_ASSIGN_REF
       mov      rdi, gword ptr [rbx+0x40]
       cmp      r14, rdi
       je       SHORT G_M_IG10
       mov      rsi, r14
       call     [GameplayTags.GameplayTagContainerAlgorithms:CopyValidated(GameplayTags.GameplayTagContainer,GameplayTags.GameplayTagContainer)]
 
G_M_IG10:                ;; offset=0x018A
       mov      rdi, gword ptr [r14+0x08]
       inc      dword ptr [rdi+0x14]
       mov      edx, dword ptr [rdi+0x10]
       xor      esi, esi
       mov      dword ptr [rdi+0x10], esi
       test     edx, edx
       jle      SHORT G_M_IG11
       mov      rdi, gword ptr [rdi+0x08]
       xor      esi, esi
       call     [System.Array:Clear(System.Array,int,int)]
 
G_M_IG11:                ;; offset=0x01A9
       lea      rdi, bword ptr [rbx+0x48]
       mov      rsi, r14
       call     CORINFO_HELP_ASSIGN_REF
       mov      rdi, qword ptr [r12]
       mov      rax, gword ptr [rbx+0x38]
       mov      rax, gword ptr [rax+0x08]
       movsxd   rax, dword ptr [rax+0x10]
       add      rdi, rax
       mov      qword ptr [r12], rdi
 
G_M_IG12:                ;; offset=0x01CC
       add      rsp, 8
       pop      rbx
       pop      r12
       pop      r13
       pop      r14
       pop      r15
       pop      rbp
       ret      
 
G_M_IG13:                ;; offset=0x01DB
       mov      rdi, r15
       test     rdi, rdi
       je       SHORT G_M_IG19
 
G_M_IG14:                ;; offset=0x01E3
       cmp      dword ptr [rdi+0x08], 7
       jne      SHORT G_M_IG19
 
G_M_IG15:                ;; offset=0x01E9
       mov      rax, 0x74006E006F0063
       xor      rax, qword ptr [rdi+0x0C]
       mov      rcx, 0x6C006F00720074
       xor      rcx, qword ptr [rdi+0x12]
       or       rax, rcx
       jne      SHORT G_M_IG19
 
G_M_IG16:                ;; offset=0x020A
       test     byte  ptr [(reloc 0xNATIVE_POINTER)], 1
       je       G_M_IG41
 
G_M_IG17:                ;; offset=0x0217
       mov      r12, 0xNATIVE_POINTER
       mov      r15, qword ptr [r12]
       mov      rdi, gword ptr [rbx+0x18]
       call     [Program:Control(GameplayTags.GameplayTag[]):long]
       add      rax, r15
       mov      qword ptr [r12], rax
 
G_M_IG18:                ;; offset=0x0236
       add      rsp, 8
       pop      rbx
       pop      r12
       pop      r13
       pop      r14
       pop      r15
       pop      rbp
       ret      
 
G_M_IG19:                ;; offset=0x0245
       test     r15, r15
       je       G_M_IG28
 
G_M_IG20:                ;; offset=0x024E
       cmp      dword ptr [r15+0x08], 9
       jne      G_M_IG28
 
G_M_IG21:                ;; offset=0x0259
       vmovups  xmm0, xmmword ptr [r15+0x0C]
       vpxor    xmm0, xmm0, xmmword ptr [reloc @RWD32]
       vmovups  xmm1, xmmword ptr [r15+0x0E]
       vpternlogq xmm0, xmm1, xmmword ptr [reloc @RWD48], -10
       vptest   xmm0, xmm0
       sete     dil
       movzx    rdi, dil
       test     edi, edi
       je       G_M_IG28
       xor      r15d, r15d
       mov      rdi, gword ptr [rbx+0x50]
       cmp      byte  ptr [rdi], dil
       xor      r13d, r13d
       mov      rdi, gword ptr [rdi+0x08]
       mov      esi, dword ptr [rdi+0x10]
       test     esi, esi
       jle      SHORT G_M_IG25
       align    [0 bytes for IG22]
 
G_M_IG22:                ;; offset=0x02A5
       mov      eax, r13d
       mov      rcx, rdi
       cmp      eax, esi
       jae      G_M_IG43
       mov      rcx, gword ptr [rcx+0x08]
       cmp      eax, dword ptr [rcx+0x08]
       jae      G_M_IG45
       mov      rax, gword ptr [rcx+8*rax+0x10]
       test     rax, rax
       jne      SHORT G_M_IG24
 
G_M_IG23:                ;; offset=0x02CA
       mov      rax, 0xNATIVE_POINTER
 
G_M_IG24:                ;; offset=0x02D4
       mov      eax, dword ptr [rax+0x08]
       add      r15, rax
       inc      r13d
       cmp      r13d, esi
       jl       SHORT G_M_IG22
 
G_M_IG25:                ;; offset=0x02E2
       test     byte  ptr [(reloc 0xNATIVE_POINTER)], 1
       je       G_M_IG42
 
G_M_IG26:                ;; offset=0x02EF
       mov      r12, 0xNATIVE_POINTER
       add      qword ptr [r12], r15
 
G_M_IG27:                ;; offset=0x02FD
       add      rsp, 8
       pop      rbx
       pop      r12
       pop      r13
       pop      r14
       pop      r15
       pop      rbp
       ret      
 
G_M_IG28:                ;; offset=0x030C
       cmp      byte  ptr [rbx+0x90], 0
       jne      SHORT G_M_IG33
       mov      rdi, gword ptr [rbx+0x60]
       test     rdi, rdi
       je       SHORT G_M_IG32
 
G_M_IG29:                ;; offset=0x031E
       cmp      dword ptr [rdi+0x08], 3
       jne      SHORT G_M_IG32
 
G_M_IG30:                ;; offset=0x0324
       mov      esi, dword ptr [rdi+0x0C]
       xor      esi, 0x65006E
       mov      edi, dword ptr [rdi+0x0E]
       xor      edi, 0x770065
       or       edi, esi
       jne      SHORT G_M_IG32
 
G_M_IG31:                ;; offset=0x033A
       mov      rdi, gword ptr [rbx+0x18]
       mov      esi, dword ptr [rbx+0x8C]
       call     [Program:Build(GameplayTags.GameplayTag[],int):GameplayTags.GameplayTagContainer]
       mov      r13, rax
       jmp      SHORT G_M_IG34
 
G_M_IG32:                ;; offset=0x034F
       mov      r13, gword ptr [rbx+0x68]
       jmp      SHORT G_M_IG34
 
G_M_IG33:                ;; offset=0x0355
       mov      rdi, gword ptr [rbx+0x58]
       cmp      esi, dword ptr [rdi+0x08]
       jae      G_M_IG45
       mov      esi, esi
       mov      r13, gword ptr [rdi+8*rsi+0x10]
 
G_M_IG34:                ;; offset=0x0369
       mov      rdi, gword ptr [rbx+0x60]
       test     rdi, rdi
       je       SHORT G_M_IG37
 
G_M_IG35:                ;; offset=0x0372
       cmp      dword ptr [rdi+0x08], 8
       jne      SHORT G_M_IG37
 
G_M_IG36:                ;; offset=0x0378
       vmovups  xmm0, xmmword ptr [rdi+0x0C]
       vpxor    xmm0, xmm0, xmmword ptr [reloc @RWD64]
       vptest   xmm0, xmm0
       sete     dil
       movzx    rdi, dil
       test     edi, edi
       je       SHORT G_M_IG37
       mov      rdi, gword ptr [rbx+0x50]
       cmp      byte  ptr [r13], r13b
       cmp      r13, rdi
       je       SHORT G_M_IG37
       mov      rsi, r13
       call     [GameplayTags.GameplayTagContainerAlgorithms:CopyValidated(GameplayTags.GameplayTagContainer,GameplayTags.GameplayTagContainer)]
 
G_M_IG37:                ;; offset=0x03AC
       mov      rax, gword ptr [rbx+0x70]
       mov      rsi, r13
       mov      rdi, gword ptr [rax+0x08]
       call     [rax+0x18]System.Action`1[System.__Canon]:Invoke(System.__Canon):this
       test     byte  ptr [(reloc 0xNATIVE_POINTER)], 1
       je       G_M_IG44
 
G_M_IG38:                ;; offset=0x03C7
       mov      r12, 0xNATIVE_POINTER
       mov      r14, qword ptr [r12]
       mov      rdi, r13
       call     [Program:Consume(GameplayTags.GameplayTagContainer):long]
       add      rax, r14
       mov      qword ptr [r12], rax
       lea      rdi, bword ptr [rbx+0x48]
       mov      rsi, r13
       call     CORINFO_HELP_ASSIGN_REF
       nop      
 
G_M_IG39:                ;; offset=0x03F2
       add      rsp, 8
       pop      rbx
       pop      r12
       pop      r13
       pop      r14
       pop      r15
       pop      rbp
       ret      
 
G_M_IG40:                ;; offset=0x0401
       mov      rdi, 0xNATIVE_POINTER
       mov      esi, 5
       call     CORINFO_HELP_GETSHARED_NONGCSTATIC_BASE
       jmp      G_M_IG05
 
G_M_IG41:                ;; offset=0x041A
       mov      rdi, 0xNATIVE_POINTER
       mov      esi, 5
       call     CORINFO_HELP_GETSHARED_NONGCSTATIC_BASE
       jmp      G_M_IG17
 
G_M_IG42:                ;; offset=0x0433
       mov      rdi, 0xNATIVE_POINTER
       mov      esi, 5
       call     CORINFO_HELP_GETSHARED_NONGCSTATIC_BASE
       jmp      G_M_IG26
 
G_M_IG43:                ;; offset=0x044C
       call     [System.ThrowHelper:ThrowArgumentOutOfRange_IndexMustBeLessException()]
       int3     
 
G_M_IG44:                ;; offset=0x0453
       mov      rdi, 0xNATIVE_POINTER
       mov      esi, 5
       call     CORINFO_HELP_GETSHARED_NONGCSTATIC_BASE
       jmp      G_M_IG38
 
G_M_IG45:                ;; offset=0x046C
       call     CORINFO_HELP_RNGCHKFAIL
       int3     
 
RWD00  	dq	006500660069006Ch, 006C006300790063h
RWD16  	dq	0063006500660069h, 0065006C00630079h
RWD32  	dq	006D0075006E0065h, 0074006100720065h
RWD48  	dq	0065006D0075006Eh, 0065007400610072h
RWD64  	dq	0061007400650072h, 00640065006E0069h

; Total bytes of code 1138



; Assembly listing for method System.Collections.Generic.List`1[GameplayTags.GameplayTag]:AddWithResize(GameplayTags.GameplayTag):this (FullOpts)
; Emitting BLENDED_CODE for X64 with AVX512 - Unix
; FullOpts code
; optimized code
; rbp based frame
; partially interruptible
; No PGO data
; 0 inlinees with PGO data; 0 single block inlinees; 1 inlinees without PGO data

G_M_IG01:                ;; offset=0x0000
       push     rbp
       push     r15
       push     r14
       push     r13
       push     rbx
       lea      rbp, [rsp+0x20]
       mov      rbx, rdi
       mov      r15, rsi
 
G_M_IG02:                ;; offset=0x0013
       mov      r14d, dword ptr [rbx+0x10]
       lea      r13d, [r14+0x01]
       mov      edi, r13d
       mov      rsi, gword ptr [rbx+0x08]
       cmp      dword ptr [rsi+0x08], 0
       je       SHORT G_M_IG04
 
G_M_IG03:                ;; offset=0x0028
       mov      esi, dword ptr [rsi+0x08]
       add      esi, esi
       jmp      SHORT G_M_IG05
 
G_M_IG04:                ;; offset=0x002F
       mov      esi, 4
 
G_M_IG05:                ;; offset=0x0034
       mov      eax, 0x7FFFFFC7
       cmp      esi, 0x7FFFFFC7
       cmova    esi, eax
       cmp      esi, edi
       cmovl    esi, edi
       mov      rdi, rbx
       call     [System.Collections.Generic.List`1[GameplayTags.GameplayTag]:set_Capacity(int):this]
       mov      dword ptr [rbx+0x10], r13d
       mov      rdi, gword ptr [rbx+0x08]
       cmp      r14d, dword ptr [rdi+0x08]
       jae      SHORT G_M_IG07
       mov      esi, r14d
       lea      rdi, bword ptr [rdi+8*rsi+0x10]
       mov      rsi, r15
       call     CORINFO_HELP_ASSIGN_REF
       nop      
 
G_M_IG06:                ;; offset=0x006F
       pop      rbx
       pop      r13
       pop      r14
       pop      r15
       pop      rbp
       ret      
 
G_M_IG07:                ;; offset=0x0078
       call     CORINFO_HELP_RNGCHKFAIL
       int3     
 
; Total bytes of code 126



; Assembly listing for method GameplayTags.GameplayTagContainer:Union(GameplayTags.GameplayTagContainer,GameplayTags.GameplayTagContainer):GameplayTags.GameplayTagContainer (FullOpts)
; Emitting BLENDED_CODE for X64 with AVX512 - Unix
; FullOpts code
; optimized code
; rbp based frame
; partially interruptible
; No PGO data
; 0 inlinees with PGO data; 7 single block inlinees; 0 inlinees without PGO data

G_M_IG01:                ;; offset=0x0000
       push     rbp
       push     r15
       push     r14
       push     r13
       push     r12
       push     rbx
       push     rax
       lea      rbp, [rsp+0x30]
       mov      rbx, rdi
       mov      r15, rsi
 
G_M_IG02:                ;; offset=0x0016
       test     rbx, rbx
       jne      SHORT G_M_IG04
 
G_M_IG03:                ;; offset=0x001B
       xor      r14d, r14d
       jmp      SHORT G_M_IG05
 
G_M_IG04:                ;; offset=0x0020
       mov      rdi, gword ptr [rbx+0x08]
       mov      r14d, dword ptr [rdi+0x10]
 
G_M_IG05:                ;; offset=0x0028
       test     r15, r15
       jne      SHORT G_M_IG07
 
G_M_IG06:                ;; offset=0x002D
       xor      r13d, r13d
       jmp      SHORT G_M_IG08
 
G_M_IG07:                ;; offset=0x0032
       mov      rdi, gword ptr [r15+0x08]
       mov      r13d, dword ptr [rdi+0x10]
 
G_M_IG08:                ;; offset=0x003A
       mov      rdi, 0xNATIVE_POINTER
       call     CORINFO_HELP_NEWSFAST
       mov      r12, rax
       mov      rdi, 0xNATIVE_POINTER
       call     CORINFO_HELP_NEWSFAST
       mov      gword ptr [rbp-0x30], rax
       lea      esi, [r14+r13]
       mov      rdi, rax
       call     [System.Collections.Generic.List`1[GameplayTags.GameplayTag]:.ctor(int):this]
       lea      rdi, bword ptr [r12+0x08]
       mov      rsi, gword ptr [rbp-0x30]
       call     CORINFO_HELP_ASSIGN_REF
       mov      rdi, rbx
       mov      rsi, r15
       mov      rdx, r12
       call     [GameplayTags.GameplayTagContainerAlgorithms:UnionSorted(GameplayTags.GameplayTagContainer,GameplayTags.GameplayTagContainer,GameplayTags.GameplayTagContainer)]
       mov      rax, r12
 
G_M_IG09:                ;; offset=0x008C
       add      rsp, 8
       pop      rbx
       pop      r12
       pop      r13
       pop      r14
       pop      r15
       pop      rbp
       ret      
 
; Total bytes of code 155



; Assembly listing for method GameplayTags.GameplayTagContainerAlgorithms:UnionSorted(GameplayTags.GameplayTagContainer,GameplayTags.GameplayTagContainer,GameplayTags.GameplayTagContainer) (FullOpts)
; Emitting BLENDED_CODE for X64 with AVX512 - Unix
; FullOpts code
; optimized code
; rbp based frame
; fully interruptible
; No PGO data
; 0 inlinees with PGO data; 27 single block inlinees; 15 inlinees without PGO data

G_M_IG01:                ;; offset=0x0000
       push     rbp
       push     r15
       push     r14
       push     r13
       push     r12
       push     rbx
       push     rax
       lea      rbp, [rsp+0x30]
       mov      r15, rdi
       mov      r14, rsi
       mov      rbx, rdx
 
G_M_IG02:                ;; offset=0x0019
       test     rbx, rbx
       je       G_M_IG47
       mov      rdi, r15
       call     [GameplayTags.GameplayTagContainerAlgorithms:AllRegistered(GameplayTags.GameplayTagContainer):bool]
       test     eax, eax
       je       SHORT G_M_IG04
 
G_M_IG03:                ;; offset=0x002F
       cmp      r15, r14
       je       G_M_IG15
       mov      rdi, r14
       call     [GameplayTags.GameplayTagContainerAlgorithms:AllRegistered(GameplayTags.GameplayTagContainer):bool]
       test     eax, eax
       jne      G_M_IG15
 
G_M_IG04:                ;; offset=0x0049
       cmp      r15, rbx
       je       SHORT G_M_IG05
       mov      r13, r15
       jmp      SHORT G_M_IG06
 
G_M_IG05:                ;; offset=0x0053
       mov      rdi, r15
       call     [GameplayTags.GameplayTagContainerAlgorithms:Snapshot(GameplayTags.GameplayTagContainer):GameplayTags.GameplayTagContainer]
       mov      r13, rax
 
G_M_IG06:                ;; offset=0x005F
       cmp      r14, rbx
       je       SHORT G_M_IG07
       jmp      SHORT G_M_IG09
 
G_M_IG07:                ;; offset=0x0066
       cmp      r15, r14
       je       SHORT G_M_IG08
       mov      rdi, r14
       call     [GameplayTags.GameplayTagContainerAlgorithms:Snapshot(GameplayTags.GameplayTagContainer):GameplayTags.GameplayTagContainer]
       mov      r14, rax
       jmp      SHORT G_M_IG09
 
G_M_IG08:                ;; offset=0x0079
       mov      r14, r13
 
G_M_IG09:                ;; offset=0x007C
       mov      rdi, gword ptr [rbx+0x08]
       inc      dword ptr [rdi+0x14]
       mov      edx, dword ptr [rdi+0x10]
       xor      esi, esi
       mov      dword ptr [rdi+0x10], esi
       test     edx, edx
       jle      SHORT G_M_IG10
       mov      rdi, gword ptr [rdi+0x08]
       xor      esi, esi
       call     [System.Array:Clear(System.Array,int,int)]
 
G_M_IG10:                ;; offset=0x009B
       test     r13, r13
       je       SHORT G_M_IG12
       xor      r15d, r15d
       mov      rdi, gword ptr [r13+0x08]
       cmp      dword ptr [rdi+0x10], 0
       jle      SHORT G_M_IG12
 
G_M_IG11:                ;; offset=0x00AD
       mov      rdi, gword ptr [r13+0x08]
       cmp      r15d, dword ptr [rdi+0x10]
       jae      G_M_IG48
       mov      rdi, gword ptr [rdi+0x08]
       cmp      r15d, dword ptr [rdi+0x08]
       jae      G_M_IG49
       mov      esi, r15d
       mov      rsi, gword ptr [rdi+8*rsi+0x10]
       mov      rdi, rbx
       call     [GameplayTags.GameplayTagContainer:AddTag(GameplayTags.GameplayTag):bool:this]
       inc      r15d
       mov      rdi, gword ptr [r13+0x08]
       cmp      r15d, dword ptr [rdi+0x10]
       jl       SHORT G_M_IG11
 
G_M_IG12:                ;; offset=0x00E7
       test     r14, r14
       je       G_M_IG45
       xor      r15d, r15d
       mov      rdi, gword ptr [r14+0x08]
       cmp      dword ptr [rdi+0x10], 0
       jle      G_M_IG45
 
G_M_IG13:                ;; offset=0x0101
       mov      rdi, gword ptr [r14+0x08]
       cmp      r15d, dword ptr [rdi+0x10]
       jae      G_M_IG48
       mov      rdi, gword ptr [rdi+0x08]
       cmp      r15d, dword ptr [rdi+0x08]
       jae      G_M_IG49
       mov      esi, r15d
       mov      rsi, gword ptr [rdi+8*rsi+0x10]
       mov      rdi, rbx
       call     [GameplayTags.GameplayTagContainer:AddTag(GameplayTags.GameplayTag):bool:this]
       inc      r15d
       mov      rdi, gword ptr [r14+0x08]
       cmp      r15d, dword ptr [rdi+0x10]
       jl       SHORT G_M_IG13
 
G_M_IG14:                ;; offset=0x013B
       jmp      G_M_IG45
 
G_M_IG15:                ;; offset=0x0140
       cmp      r15, r14
       jne      SHORT G_M_IG17
       cmp      r15, rbx
       je       G_M_IG45
       mov      rdi, gword ptr [rbx+0x08]
       inc      dword ptr [rdi+0x14]
       mov      edx, dword ptr [rdi+0x10]
       xor      esi, esi
       mov      dword ptr [rdi+0x10], esi
       test     edx, edx
       jle      SHORT G_M_IG16
       mov      rdi, gword ptr [rdi+0x08]
       xor      esi, esi
       call     [System.Array:Clear(System.Array,int,int)]
 
G_M_IG16:                ;; offset=0x016D
       test     r15, r15
       je       G_M_IG45
       mov      rsi, gword ptr [r15+0x08]
       mov      esi, dword ptr [rsi+0x10]
       mov      rdi, rbx
       call     [GameplayTags.GameplayTagContainer:EnsureCapacity(int):this]
       mov      rdi, gword ptr [rbx+0x08]
       mov      rsi, gword ptr [r15+0x08]
       cmp      dword ptr [rdi], edi
       call     [System.Collections.Generic.List`1[GameplayTags.GameplayTag]:AddRange(System.Collections.Generic.IEnumerable`1[GameplayTags.GameplayTag]):this]
       jmp      G_M_IG45
 
G_M_IG17:                ;; offset=0x019B
       cmp      r15, rbx
       jne      SHORT G_M_IG18
       mov      rdi, rbx
       mov      rsi, r14
       xor      edx, edx
       call     [GameplayTags.GameplayTagContainerAlgorithms:AppendAlreadyValidated(GameplayTags.GameplayTagContainer,GameplayTags.GameplayTagContainer,bool)]
       jmp      G_M_IG45
 
G_M_IG18:                ;; offset=0x01B3
       cmp      r14, rbx
       jne      SHORT G_M_IG19
       mov      rdi, rbx
       mov      rsi, r15
       mov      edx, 1
       call     [GameplayTags.GameplayTagContainerAlgorithms:AppendAlreadyValidated(GameplayTags.GameplayTagContainer,GameplayTags.GameplayTagContainer,bool)]
       jmp      G_M_IG45
 
G_M_IG19:                ;; offset=0x01CE
       test     r15, r15
       jne      SHORT G_M_IG20
       xor      r13, r13
       jmp      SHORT G_M_IG21
 
G_M_IG20:                ;; offset=0x01D8
       mov      r13, gword ptr [r15+0x08]
 
G_M_IG21:                ;; offset=0x01DC
       test     r14, r14
       jne      SHORT G_M_IG22
       xor      r15, r15
       jmp      SHORT G_M_IG23
 
G_M_IG22:                ;; offset=0x01E6
       mov      r15, gword ptr [r14+0x08]
 
G_M_IG23:                ;; offset=0x01EA
       mov      rdi, gword ptr [rbx+0x08]
       inc      dword ptr [rdi+0x14]
       mov      edx, dword ptr [rdi+0x10]
       xor      esi, esi
       mov      dword ptr [rdi+0x10], esi
       test     edx, edx
       jle      SHORT G_M_IG24
       mov      rdi, gword ptr [rdi+0x08]
       xor      esi, esi
       call     [System.Array:Clear(System.Array,int,int)]
 
G_M_IG24:                ;; offset=0x0209
       test     r13, r13
       jne      SHORT G_M_IG25
       xor      esi, esi
       jmp      SHORT G_M_IG26
 
G_M_IG25:                ;; offset=0x0212
       mov      esi, dword ptr [r13+0x10]
 
G_M_IG26:                ;; offset=0x0216
       test     r15, r15
       jne      SHORT G_M_IG27
       xor      edi, edi
       jmp      SHORT G_M_IG28
 
G_M_IG27:                ;; offset=0x021F
       mov      edi, dword ptr [r15+0x10]
 
G_M_IG28:                ;; offset=0x0223
       add      esi, edi
       mov      rdi, rbx
       call     [GameplayTags.GameplayTagContainer:EnsureCapacity(int):this]
       test     r13, r13
       jne      SHORT G_M_IG29
       test     r15, r15
       je       G_M_IG45
       mov      rdi, gword ptr [rbx+0x08]
       mov      rsi, r15
       cmp      dword ptr [rdi], edi
       call     [System.Collections.Generic.List`1[GameplayTags.GameplayTag]:AddRange(System.Collections.Generic.IEnumerable`1[GameplayTags.GameplayTag]):this]
       jmp      G_M_IG45
 
G_M_IG29:                ;; offset=0x0250
       test     r15, r15
       jne      SHORT G_M_IG30
       mov      rdi, gword ptr [rbx+0x08]
       mov      rsi, r13
       cmp      dword ptr [rdi], edi
       call     [System.Collections.Generic.List`1[GameplayTags.GameplayTag]:AddRange(System.Collections.Generic.IEnumerable`1[GameplayTags.GameplayTag]):this]
       jmp      G_M_IG45
 
G_M_IG30:                ;; offset=0x0269
       xor      r14d, r14d
       xor      r12d, r12d
       cmp      dword ptr [r13+0x10], 0
       jle      SHORT G_M_IG33
 
G_M_IG31:                ;; offset=0x0276
       mov      edi, dword ptr [r13+0x10]
       cmp      r14d, edi
       jne      SHORT G_M_IG34
 
G_M_IG32:                ;; offset=0x027F
       lea      edi, [r12+0x01]
       mov      eax, edi
       cmp      r12d, dword ptr [r15+0x10]
       jae      G_M_IG48
       mov      rdi, gword ptr [r15+0x08]
       cmp      r12d, dword ptr [rdi+0x08]
       jae      G_M_IG49
       mov      esi, r12d
       mov      rsi, gword ptr [rdi+8*rsi+0x10]
       jmp      G_M_IG40
 
G_M_IG33:                ;; offset=0x02AB
       mov      dword ptr [rbp-0x2C], r12d
       jmp      G_M_IG44
 
G_M_IG34:                ;; offset=0x02B4
       cmp      r12d, dword ptr [r15+0x10]
       jne      SHORT G_M_IG35
       lea      esi, [r14+0x01]
       mov      eax, esi
       cmp      r14d, edi
       jae      G_M_IG48
       mov      rdi, gword ptr [r13+0x08]
       cmp      r14d, dword ptr [rdi+0x08]
       jae      G_M_IG49
       mov      esi, r14d
       mov      rsi, gword ptr [rdi+8*rsi+0x10]
       mov      r14d, eax
       mov      eax, r12d
       jmp      G_M_IG40
 
G_M_IG35:                ;; offset=0x02EA
       cmp      r14d, edi
       jae      G_M_IG48
       mov      rdi, gword ptr [r13+0x08]
       cmp      r14d, dword ptr [rdi+0x08]
       jae      G_M_IG49
       mov      esi, r14d
       mov      rdi, gword ptr [rdi+8*rsi+0x10]
       test     rdi, rdi
       jne      SHORT G_M_IG36
       mov      rdi, 0xNATIVE_POINTER
 
G_M_IG36:                ;; offset=0x0318
       cmp      r12d, dword ptr [r15+0x10]
       jae      G_M_IG48
       mov      rsi, gword ptr [r15+0x08]
       cmp      r12d, dword ptr [rsi+0x08]
       jae      G_M_IG49
       mov      eax, r12d
       mov      rsi, gword ptr [rsi+8*rax+0x10]
       test     rsi, rsi
       jne      SHORT G_M_IG37
       mov      rsi, 0xNATIVE_POINTER
 
G_M_IG37:                ;; offset=0x0347
       call     [System.String:CompareOrdinal(System.String,System.String):int]
       test     eax, eax
       jge      SHORT G_M_IG38
       mov      eax, r14d
       lea      r14d, [rax+0x01]
       cmp      eax, dword ptr [r13+0x10]
       jae      G_M_IG48
       mov      rdi, gword ptr [r13+0x08]
       cmp      eax, dword ptr [rdi+0x08]
       jae      G_M_IG49
       mov      esi, eax
       mov      rsi, gword ptr [rdi+8*rsi+0x10]
       mov      eax, r12d
       jmp      SHORT G_M_IG40
 
G_M_IG38:                ;; offset=0x037B
       test     eax, eax
       jle      SHORT G_M_IG39
       mov      esi, r12d
       lea      r12d, [rsi+0x01]
       cmp      esi, dword ptr [r15+0x10]
       jae      G_M_IG48
       mov      rdi, gword ptr [r15+0x08]
       cmp      esi, dword ptr [rdi+0x08]
       jae      G_M_IG49
       mov      rsi, gword ptr [rdi+8*rsi+0x10]
       mov      eax, r12d
       jmp      SHORT G_M_IG40
 
G_M_IG39:                ;; offset=0x03A7
       mov      esi, r14d
       lea      r14d, [rsi+0x01]
       cmp      esi, dword ptr [r13+0x10]
       jae      G_M_IG48
       mov      rdi, gword ptr [r13+0x08]
       cmp      esi, dword ptr [rdi+0x08]
       jae      G_M_IG49
       mov      rsi, gword ptr [rdi+8*rsi+0x10]
       inc      r12d
       mov      dword ptr [rbp-0x2C], r12d
       mov      eax, dword ptr [rbp-0x2C]
 
G_M_IG40:                ;; offset=0x03D4
       mov      rdi, gword ptr [rbx+0x08]
       inc      dword ptr [rdi+0x14]
       mov      rcx, gword ptr [rdi+0x08]
       mov      edx, dword ptr [rdi+0x10]
       cmp      dword ptr [rcx+0x08], edx
       jbe      SHORT G_M_IG42
 
G_M_IG41:                ;; offset=0x03E7
       mov      dword ptr [rbp-0x2C], eax
       lea      r8d, [rdx+0x01]
       mov      dword ptr [rdi+0x10], r8d
       mov      edi, edx
       lea      rdi, bword ptr [rcx+8*rdi+0x10]
       call     CORINFO_HELP_ASSIGN_REF
       jmp      SHORT G_M_IG43
 
G_M_IG42:                ;; offset=0x0400
       mov      dword ptr [rbp-0x2C], eax
       call     [System.Collections.Generic.List`1[GameplayTags.GameplayTag]:AddWithResize(GameplayTags.GameplayTag):this]
 
G_M_IG43:                ;; offset=0x0409
       cmp      r14d, dword ptr [r13+0x10]
       jl       SHORT G_M_IG46
 
G_M_IG44:                ;; offset=0x040F
       mov      r12d, dword ptr [rbp-0x2C]
       cmp      r12d, dword ptr [r15+0x10]
       jl       G_M_IG31
 
G_M_IG45:                ;; offset=0x041D
       add      rsp, 8
       pop      rbx
       pop      r12
       pop      r13
       pop      r14
       pop      r15
       pop      rbp
       ret      
 
G_M_IG46:                ;; offset=0x042C
       mov      r12d, dword ptr [rbp-0x2C]
       jmp      G_M_IG31
 
G_M_IG47:                ;; offset=0x0435
       mov      rdi, 0xNATIVE_POINTER
       call     CORINFO_HELP_NEWSFAST
       mov      rbx, rax
       mov      edi, 0x952
       mov      rsi, 0xNATIVE_POINTER
       call     CORINFO_HELP_STRCNS
       mov      rsi, rax
       mov      rdi, rbx
       call     [System.ArgumentNullException:.ctor(System.String):this]
       mov      rdi, rbx
       call     CORINFO_HELP_THROW
 
G_M_IG48:                ;; offset=0x046F
       call     [System.ThrowHelper:ThrowArgumentOutOfRange_IndexMustBeLessException()]
       int3     
 
G_M_IG49:                ;; offset=0x0476
       call     CORINFO_HELP_RNGCHKFAIL
       int3     
 
; Total bytes of code 1148



; Assembly listing for method GameplayTags.GameplayTagContainerAlgorithms:AllRegistered(GameplayTags.GameplayTagContainer):bool (FullOpts)
; Emitting BLENDED_CODE for X64 with AVX512 - Unix
; FullOpts code
; optimized code
; rbp based frame
; partially interruptible
; No PGO data
; 0 inlinees with PGO data; 7 single block inlinees; 2 inlinees without PGO data

G_M_IG01:                ;; offset=0x0000
       push     rbp
       push     r15
       push     rbx
       lea      rbp, [rsp+0x10]
       mov      rbx, rdi
 
G_M_IG02:                ;; offset=0x000C
       test     rbx, rbx
       je       SHORT G_M_IG04
 
G_M_IG03:                ;; offset=0x0011
       mov      rax, gword ptr [rbx+0x08]
       mov      edi, dword ptr [rax+0x10]
       test     edi, edi
       jne      SHORT G_M_IG06
 
G_M_IG04:                ;; offset=0x001C
       mov      eax, 1
 
G_M_IG05:                ;; offset=0x0021
       pop      rbx
       pop      r15
       pop      rbp
       ret      
 
G_M_IG06:                ;; offset=0x0026
       mov      rax, 0xNATIVE_POINTER
       cmp      gword ptr [rax], 0
       jne      SHORT G_M_IG08
       xor      eax, eax
 
G_M_IG07:                ;; offset=0x0038
       pop      rbx
       pop      r15
       pop      rbp
       ret      
 
G_M_IG08:                ;; offset=0x003D
       xor      r15d, r15d
       test     edi, edi
       jle      SHORT G_M_IG12
 
G_M_IG09:                ;; offset=0x0044
       mov      rdi, gword ptr [rbx+0x08]
       cmp      r15d, dword ptr [rdi+0x10]
       jae      SHORT G_M_IG16
       mov      rdi, gword ptr [rdi+0x08]
       cmp      r15d, dword ptr [rdi+0x08]
       jae      SHORT G_M_IG17
       mov      eax, r15d
       mov      rdi, gword ptr [rdi+8*rax+0x10]
       test     rdi, rdi
       jne      SHORT G_M_IG11
 
G_M_IG10:                ;; offset=0x0065
       mov      rdi, 0xNATIVE_POINTER
 
G_M_IG11:                ;; offset=0x006F
       call     [GameplayTags.GameplayTagManager:IsRegistered(System.String):bool]
       test     eax, eax
       je       SHORT G_M_IG14
       inc      r15d
       mov      rax, gword ptr [rbx+0x08]
       cmp      r15d, dword ptr [rax+0x10]
       jl       SHORT G_M_IG09
 
G_M_IG12:                ;; offset=0x0086
       mov      eax, 1
 
G_M_IG13:                ;; offset=0x008B
       pop      rbx
       pop      r15
       pop      rbp
       ret      
 
G_M_IG14:                ;; offset=0x0090
       xor      eax, eax
 
G_M_IG15:                ;; offset=0x0092
       pop      rbx
       pop      r15
       pop      rbp
       ret      
 
G_M_IG16:                ;; offset=0x0097
       call     [System.ThrowHelper:ThrowArgumentOutOfRange_IndexMustBeLessException()]
       int3     
 
G_M_IG17:                ;; offset=0x009E
       call     CORINFO_HELP_RNGCHKFAIL
       int3     
 
; Total bytes of code 164



; Assembly listing for method GameplayTags.GameplayTagContainer:EnsureCapacity(int):this (FullOpts)
; Emitting BLENDED_CODE for X64 with AVX512 - Unix
; FullOpts code
; optimized code
; rbp based frame
; fully interruptible
; No PGO data
; 0 inlinees with PGO data; 1 single block inlinees; 0 inlinees without PGO data

G_M_IG01:                ;; offset=0x0000
       push     rbp
       push     rbx
       push     rax
       lea      rbp, [rsp+0x10]
 
G_M_IG02:                ;; offset=0x0008
       test     esi, esi
       jl       SHORT G_M_IG05
       mov      rdi, gword ptr [rdi+0x08]
       mov      rax, gword ptr [rdi+0x08]
       cmp      dword ptr [rax+0x08], esi
       jge      SHORT G_M_IG04
 
G_M_IG03:                ;; offset=0x0019
       add      rsp, 8
       pop      rbx
       pop      rbp
       tail.jmp [System.Collections.Generic.List`1[GameplayTags.GameplayTag]:set_Capacity(int):this]
 
G_M_IG04:                ;; offset=0x0025
       add      rsp, 8
       pop      rbx
       pop      rbp
       ret      
 
G_M_IG05:                ;; offset=0x002C
       mov      rdi, 0xNATIVE_POINTER
       call     CORINFO_HELP_NEWSFAST
       mov      rbx, rax
       mov      edi, 0x930
       mov      rsi, 0xNATIVE_POINTER
       call     CORINFO_HELP_STRCNS
       mov      rsi, rax
       mov      rdi, rbx
       call     [System.ArgumentOutOfRangeException:.ctor(System.String):this]
       mov      rdi, rbx
       call     CORINFO_HELP_THROW
       int3     
 
; Total bytes of code 103



; Assembly listing for method GameplayTags.GameplayTagContainer:IntersectionExact(GameplayTags.GameplayTagContainer,GameplayTags.GameplayTagContainer):GameplayTags.GameplayTagContainer (FullOpts)
; Emitting BLENDED_CODE for X64 with AVX512 - Unix
; FullOpts code
; optimized code
; rbp based frame
; partially interruptible
; No PGO data
; 1 inlinees with PGO data; 7 single block inlinees; 0 inlinees without PGO data

G_M_IG01:                ;; offset=0x0000
       push     rbp
       push     r15
       push     r14
       push     r13
       push     r12
       push     rbx
       push     rax
       lea      rbp, [rsp+0x30]
       mov      rbx, rdi
       mov      r15, rsi
 
G_M_IG02:                ;; offset=0x0016
       test     rbx, rbx
       jne      SHORT G_M_IG04
 
G_M_IG03:                ;; offset=0x001B
       xor      r14d, r14d
       jmp      SHORT G_M_IG05
 
G_M_IG04:                ;; offset=0x0020
       mov      rdi, gword ptr [rbx+0x08]
       mov      r14d, dword ptr [rdi+0x10]
 
G_M_IG05:                ;; offset=0x0028
       test     r15, r15
       jne      SHORT G_M_IG07
 
G_M_IG06:                ;; offset=0x002D
       xor      edi, edi
       jmp      SHORT G_M_IG08
 
G_M_IG07:                ;; offset=0x0031
       mov      rdi, gword ptr [r15+0x08]
       mov      edi, dword ptr [rdi+0x10]
 
G_M_IG08:                ;; offset=0x0038
       cmp      r14d, edi
       cmovg    r14d, edi
       mov      rdi, 0xNATIVE_POINTER
       call     CORINFO_HELP_NEWSFAST
       mov      r13, rax
       mov      rdi, 0xNATIVE_POINTER
       call     CORINFO_HELP_NEWSFAST
       mov      r12, rax
       mov      rdi, r12
       mov      esi, r14d
       call     [System.Collections.Generic.List`1[GameplayTags.GameplayTag]:.ctor(int):this]
       lea      rdi, bword ptr [r13+0x08]
       mov      rsi, r12
       call     CORINFO_HELP_ASSIGN_REF
       mov      rdi, rbx
       mov      rsi, r15
       mov      rdx, r13
       call     [GameplayTags.GameplayTagContainerAlgorithms:IntersectionExactSorted(GameplayTags.GameplayTagContainer,GameplayTags.GameplayTagContainer,GameplayTags.GameplayTagContainer)]
       mov      rax, r13
 
G_M_IG09:                ;; offset=0x008D
       add      rsp, 8
       pop      rbx
       pop      r12
       pop      r13
       pop      r14
       pop      r15
       pop      rbp
       ret      
 
; Total bytes of code 156



; Assembly listing for method GameplayTags.GameplayTagContainerAlgorithms:IntersectionExactSorted(GameplayTags.GameplayTagContainer,GameplayTags.GameplayTagContainer,GameplayTags.GameplayTagContainer) (FullOpts)
; Emitting BLENDED_CODE for X64 with AVX512 - Unix
; FullOpts code
; optimized code
; rbp based frame
; fully interruptible
; No PGO data
; 1 inlinees with PGO data; 11 single block inlinees; 9 inlinees without PGO data

G_M_IG01:                ;; offset=0x0000
       push     rbp
       push     r15
       push     r14
       push     r13
       push     r12
       push     rbx
       push     rax
       lea      rbp, [rsp+0x30]
       mov      rbx, rdx
 
G_M_IG02:                ;; offset=0x0013
       test     rbx, rbx
       je       G_M_IG24
       test     rdi, rdi
       je       SHORT G_M_IG04
 
G_M_IG03:                ;; offset=0x0021
       test     rsi, rsi
       jne      SHORT G_M_IG07
 
G_M_IG04:                ;; offset=0x0026
       mov      rdi, gword ptr [rbx+0x08]
       inc      dword ptr [rdi+0x14]
       mov      edx, dword ptr [rdi+0x10]
       xor      esi, esi
       mov      dword ptr [rdi+0x10], esi
       test     edx, edx
       jle      SHORT G_M_IG05
       mov      rdi, gword ptr [rdi+0x08]
       xor      esi, esi
       call     [System.Array:Clear(System.Array,int,int)]
 
G_M_IG05:                ;; offset=0x0045
       nop      
 
G_M_IG06:                ;; offset=0x0046
       add      rsp, 8
       pop      rbx
       pop      r12
       pop      r13
       pop      r14
       pop      r15
       pop      rbp
       ret      
 
G_M_IG07:                ;; offset=0x0055
       mov      r15, gword ptr [rdi+0x08]
       mov      r14, gword ptr [rsi+0x08]
       mov      r13, gword ptr [rbx+0x08]
       cmp      rbx, rdi
       je       SHORT G_M_IG08
       xor      edi, edi
       cmp      rbx, rsi
       sete     dil
       jmp      SHORT G_M_IG09
 
G_M_IG08:                ;; offset=0x0071
       mov      edi, 1
 
G_M_IG09:                ;; offset=0x0076
       movzx    r12, dil
       test     r12d, r12d
       jne      SHORT G_M_IG11
       inc      dword ptr [r13+0x14]
       mov      edx, dword ptr [r13+0x10]
       xor      edi, edi
       mov      dword ptr [r13+0x10], edi
       test     edx, edx
       jle      SHORT G_M_IG10
       mov      rdi, gword ptr [r13+0x08]
       xor      esi, esi
       call     [System.Array:Clear(System.Array,int,int)]
 
G_M_IG10:                ;; offset=0x009D
       mov      esi, dword ptr [r15+0x10]
       mov      edi, dword ptr [r14+0x10]
       cmp      esi, edi
       cmovg    esi, edi
       mov      rdi, rbx
       call     [GameplayTags.GameplayTagContainer:EnsureCapacity(int):this]
 
G_M_IG11:                ;; offset=0x00B3
       xor      ebx, ebx
       xor      eax, eax
       xor      ecx, ecx
       mov      dword ptr [rbp-0x30], ecx
       jmp      G_M_IG19
 
G_M_IG12:                ;; offset=0x00C1
       cmp      ebx, dword ptr [r15+0x10]
       jae      G_M_IG23
       mov      rdi, gword ptr [r15+0x08]
       cmp      ebx, dword ptr [rdi+0x08]
       jae      G_M_IG25
       mov      esi, ebx
       mov      rdi, gword ptr [rdi+8*rsi+0x10]
       test     rdi, rdi
       jne      SHORT G_M_IG13
       mov      rdi, 0xNATIVE_POINTER
 
G_M_IG13:                ;; offset=0x00EE
       cmp      eax, dword ptr [r14+0x10]
       jae      G_M_IG23
       mov      rsi, gword ptr [r14+0x08]
       cmp      eax, dword ptr [rsi+0x08]
       jae      G_M_IG25
       mov      dword ptr [rbp-0x2C], eax
       mov      edx, eax
       mov      rsi, gword ptr [rsi+8*rdx+0x10]
       test     rsi, rsi
       jne      SHORT G_M_IG14
       mov      rsi, 0xNATIVE_POINTER
 
G_M_IG14:                ;; offset=0x011E
       call     [System.String:CompareOrdinal(System.String,System.String):int]
       test     eax, eax
       jge      SHORT G_M_IG15
       inc      ebx
       mov      eax, dword ptr [rbp-0x2C]
       jmp      G_M_IG19
 
G_M_IG15:                ;; offset=0x0132
       test     eax, eax
       jle      SHORT G_M_IG16
       mov      eax, dword ptr [rbp-0x2C]
       inc      eax
       jmp      G_M_IG19
 
G_M_IG16:                ;; offset=0x0140
       mov      edi, ebx
       lea      ebx, [rdi+0x01]
       cmp      edi, dword ptr [r15+0x10]
       jae      G_M_IG23
       mov      rsi, gword ptr [r15+0x08]
       cmp      edi, dword ptr [rsi+0x08]
       jae      G_M_IG25
       mov      rsi, gword ptr [rsi+8*rdi+0x10]
       mov      eax, dword ptr [rbp-0x2C]
       inc      eax
       mov      dword ptr [rbp-0x2C], eax
       test     r12d, r12d
       je       SHORT G_M_IG17
       mov      edi, dword ptr [rbp-0x30]
       lea      ecx, [rdi+0x01]
       mov      dword ptr [rbp-0x30], ecx
       cmp      edi, dword ptr [r13+0x10]
       jae      G_M_IG23
       mov      rdx, gword ptr [r13+0x08]
       cmp      edi, dword ptr [rdx+0x08]
       jae      G_M_IG25
       lea      rdi, bword ptr [rdx+8*rdi+0x10]
       call     CORINFO_HELP_ASSIGN_REF
       inc      dword ptr [r13+0x14]
       mov      eax, dword ptr [rbp-0x2C]
       jmp      SHORT G_M_IG19
 
G_M_IG17:                ;; offset=0x01A1
       inc      dword ptr [r13+0x14]
       mov      rdi, gword ptr [r13+0x08]
       mov      ecx, dword ptr [r13+0x10]
       cmp      dword ptr [rdi+0x08], ecx
       jbe      SHORT G_M_IG18
       lea      edx, [rcx+0x01]
       mov      dword ptr [r13+0x10], edx
       lea      rdi, bword ptr [rdi+8*rcx+0x10]
       call     CORINFO_HELP_ASSIGN_REF
       mov      eax, dword ptr [rbp-0x2C]
       jmp      SHORT G_M_IG19
 
G_M_IG18:                ;; offset=0x01C8
       mov      rdi, r13
       call     [System.Collections.Generic.List`1[GameplayTags.GameplayTag]:AddWithResize(GameplayTags.GameplayTag):this]
       mov      eax, dword ptr [rbp-0x2C]
 
G_M_IG19:                ;; offset=0x01D4
       cmp      ebx, dword ptr [r15+0x10]
       jge      SHORT G_M_IG20
       cmp      eax, dword ptr [r14+0x10]
       jl       G_M_IG12
 
G_M_IG20:                ;; offset=0x01E4
       test     r12d, r12d
       je       SHORT G_M_IG21
       mov      edx, dword ptr [r13+0x10]
       mov      ecx, dword ptr [rbp-0x30]
       sub      edx, ecx
       mov      rdi, r13
       mov      esi, ecx
       call     [System.Collections.Generic.List`1[GameplayTags.GameplayTag]:RemoveRange(int,int):this]
 
G_M_IG21:                ;; offset=0x01FD
       nop      
 
G_M_IG22:                ;; offset=0x01FE
       add      rsp, 8
       pop      rbx
       pop      r12
       pop      r13
       pop      r14
       pop      r15
       pop      rbp
       ret      
 
G_M_IG23:                ;; offset=0x020D
       call     [System.ThrowHelper:ThrowArgumentOutOfRange_IndexMustBeLessException()]
       int3     
 
G_M_IG24:                ;; offset=0x0214
       mov      rdi, 0xNATIVE_POINTER
       call     CORINFO_HELP_NEWSFAST
       mov      rbx, rax
       mov      edi, 0x952
       mov      rsi, 0xNATIVE_POINTER
       call     CORINFO_HELP_STRCNS
       mov      rsi, rax
       mov      rdi, rbx
       call     [System.ArgumentNullException:.ctor(System.String):this]
       mov      rdi, rbx
       call     CORINFO_HELP_THROW
 
G_M_IG25:                ;; offset=0x024E
       call     CORINFO_HELP_RNGCHKFAIL
       int3     
 
; Total bytes of code 596



; Assembly listing for method GameplayTags.GameplayTagContainerAlgorithms:CopyValidated(GameplayTags.GameplayTagContainer,GameplayTags.GameplayTagContainer) (FullOpts)
; Emitting BLENDED_CODE for X64 with AVX512 - Unix
; FullOpts code
; optimized code
; rbp based frame
; partially interruptible
; No PGO data
; 0 inlinees with PGO data; 9 single block inlinees; 2 inlinees without PGO data

G_M_IG01:                ;; offset=0x0000
       push     rbp
       push     r15
       push     r14
       push     rbx
       push     rax
       lea      rbp, [rsp+0x20]
       mov      rbx, rdi
       mov      r15, rsi
 
G_M_IG02:                ;; offset=0x0012
       mov      rdi, rbx
       call     [GameplayTags.GameplayTagContainerAlgorithms:AllRegistered(GameplayTags.GameplayTagContainer):bool]
       mov      r14d, eax
       mov      rdi, gword ptr [r15+0x08]
       inc      dword ptr [rdi+0x14]
       mov      edx, dword ptr [rdi+0x10]
       xor      esi, esi
       mov      dword ptr [rdi+0x10], esi
       test     edx, edx
       jle      SHORT G_M_IG04
 
G_M_IG03:                ;; offset=0x0031
       mov      rdi, gword ptr [rdi+0x08]
       xor      esi, esi
       call     [System.Array:Clear(System.Array,int,int)]
 
G_M_IG04:                ;; offset=0x003D
       test     rbx, rbx
       jne      SHORT G_M_IG06
 
G_M_IG05:                ;; offset=0x0042
       add      rsp, 8
       pop      rbx
       pop      r14
       pop      r15
       pop      rbp
       ret      
 
G_M_IG06:                ;; offset=0x004D
       mov      rsi, gword ptr [rbx+0x08]
       mov      esi, dword ptr [rsi+0x10]
       mov      rdi, r15
       call     [GameplayTags.GameplayTagContainer:EnsureCapacity(int):this]
       test     r14d, r14d
       je       SHORT G_M_IG08
       mov      rdi, gword ptr [r15+0x08]
       mov      rsi, gword ptr [rbx+0x08]
       cmp      dword ptr [rdi], edi
 
G_M_IG07:                ;; offset=0x006C
       add      rsp, 8
       pop      rbx
       pop      r14
       pop      r15
       pop      rbp
       tail.jmp [System.Collections.Generic.List`1[GameplayTags.GameplayTag]:AddRange(System.Collections.Generic.IEnumerable`1[GameplayTags.GameplayTag]):this]
 
G_M_IG08:                ;; offset=0x007C
       xor      r14d, r14d
       mov      rdi, gword ptr [rbx+0x08]
       cmp      dword ptr [rdi+0x10], 0
       jle      SHORT G_M_IG10
 
G_M_IG09:                ;; offset=0x0089
       mov      rdi, gword ptr [rbx+0x08]
       cmp      r14d, dword ptr [rdi+0x10]
       jae      SHORT G_M_IG11
       mov      rdi, gword ptr [rdi+0x08]
       cmp      r14d, dword ptr [rdi+0x08]
       jae      SHORT G_M_IG12
       mov      esi, r14d
       mov      rsi, gword ptr [rdi+8*rsi+0x10]
       mov      rdi, r15
       call     [GameplayTags.GameplayTagContainer:AddTag(GameplayTags.GameplayTag):bool:this]
       inc      r14d
       mov      rax, gword ptr [rbx+0x08]
       cmp      r14d, dword ptr [rax+0x10]
       jl       SHORT G_M_IG09
 
G_M_IG10:                ;; offset=0x00BB
       add      rsp, 8
       pop      rbx
       pop      r14
       pop      r15
       pop      rbp
       ret      
 
G_M_IG11:                ;; offset=0x00C6
       call     [System.ThrowHelper:ThrowArgumentOutOfRange_IndexMustBeLessException()]
       int3     
 
G_M_IG12:                ;; offset=0x00CD
       call     CORINFO_HELP_RNGCHKFAIL
       int3     
 
; Total bytes of code 211



; Assembly listing for method System.Collections.Generic.List`1[GameplayTags.GameplayTag]:AddRange(System.Collections.Generic.IEnumerable`1[GameplayTags.GameplayTag]):this (FullOpts)
; Emitting BLENDED_CODE for X64 with AVX512 - Unix
; FullOpts code
; optimized code
; rbp based frame
; fully interruptible
; No PGO data
; 0 inlinees with PGO data; 0 single block inlinees; 2 inlinees without PGO data

G_M_IG01:                ;; offset=0x0000
       push     rbp
       push     r15
       push     r14
       push     rbx
       sub      rsp, 24
       lea      rbp, [rsp+0x30]
       mov      qword ptr [rbp-0x30], rsp
       mov      rbx, rdi
       mov      r15, rsi
 
G_M_IG02:                ;; offset=0x0019
       test     r15, r15
       je       G_M_IG18
       mov      rsi, r15
       mov      rdi, 0xNATIVE_POINTER
       call     [CORINFO_HELP_ISINSTANCEOFINTERFACE]
       mov      r14, rax
       test     r14, r14
       je       G_M_IG08
 
G_M_IG03:                ;; offset=0x0041
       mov      rdi, r14
       mov      r11, 0xNATIVE_POINTER
       call     [r11]System.Collections.Generic.ICollection`1[GameplayTags.GameplayTag]:get_Count():int:this
       mov      r15d, eax
       test     r15d, r15d
       jle      G_M_IG15
       mov      rdi, gword ptr [rbx+0x08]
       mov      esi, dword ptr [rdi+0x08]
       mov      eax, dword ptr [rbx+0x10]
       sub      esi, eax
       cmp      esi, r15d
       jge      SHORT G_M_IG06
       add      eax, r15d
       jo       G_M_IG17
       cmp      dword ptr [rdi+0x08], 0
       je       SHORT G_M_IG04
       mov      esi, dword ptr [rdi+0x08]
       add      esi, esi
       jmp      SHORT G_M_IG05
 
G_M_IG04:                ;; offset=0x0084
       mov      esi, 4
 
G_M_IG05:                ;; offset=0x0089
       mov      edi, 0x7FFFFFC7
       cmp      esi, 0x7FFFFFC7
       cmova    esi, edi
       cmp      esi, eax
       cmovl    esi, eax
       mov      rdi, rbx
       call     [System.Collections.Generic.List`1[GameplayTags.GameplayTag]:set_Capacity(int):this]
 
G_M_IG06:                ;; offset=0x00A5
       mov      rsi, gword ptr [rbx+0x08]
       mov      edx, dword ptr [rbx+0x10]
       mov      rdi, r14
       mov      r11, 0xNATIVE_POINTER
       call     [r11]System.Collections.Generic.ICollection`1[GameplayTags.GameplayTag]:CopyTo(GameplayTags.GameplayTag[],int):this
       add      dword ptr [rbx+0x10], r15d
       inc      dword ptr [rbx+0x14]
 
G_M_IG07:                ;; offset=0x00C3
       add      rsp, 24
       pop      rbx
       pop      r14
       pop      r15
       pop      rbp
       ret      
 
G_M_IG08:                ;; offset=0x00CE
       mov      rdi, r15
       mov      r11, 0xNATIVE_POINTER
       call     [r11]System.Collections.Generic.IEnumerable`1[GameplayTags.GameplayTag]:GetEnumerator():System.Collections.Generic.IEnumerator`1[GameplayTags.GameplayTag]:this
       mov      r15, rax
       mov      gword ptr [rbp-0x20], r15
 
G_M_IG09:                ;; offset=0x00E5
       mov      rdi, r15
       mov      r11, 0xNATIVE_POINTER
       call     [r11]System.Collections.IEnumerator:MoveNext():bool:this
       test     eax, eax
       je       SHORT G_M_IG14
 
G_M_IG10:                ;; offset=0x00F9
       mov      rdi, r15
       mov      r11, 0xNATIVE_POINTER
       call     [r11]System.Collections.Generic.IEnumerator`1[GameplayTags.GameplayTag]:get_Current():GameplayTags.GameplayTag:this
       inc      dword ptr [rbx+0x14]
       mov      rdi, gword ptr [rbx+0x08]
       mov      esi, dword ptr [rbx+0x10]
       mov      ecx, dword ptr [rdi+0x08]
       cmp      ecx, esi
       jbe      SHORT G_M_IG12
 
G_M_IG11:                ;; offset=0x011A
       lea      ecx, [rsi+0x01]
       mov      dword ptr [rbx+0x10], ecx
       mov      esi, esi
       lea      rdi, bword ptr [rdi+8*rsi+0x10]
       mov      rsi, rax
       call     CORINFO_HELP_ASSIGN_REF
       jmp      SHORT G_M_IG13
 
G_M_IG12:                ;; offset=0x0131
       mov      rdi, rbx
       mov      rsi, rax
       call     [System.Collections.Generic.List`1[GameplayTags.GameplayTag]:AddWithResize(GameplayTags.GameplayTag):this]
 
G_M_IG13:                ;; offset=0x013D
       mov      rdi, r15
       mov      r11, 0xNATIVE_POINTER
       call     [r11]System.Collections.IEnumerator:MoveNext():bool:this
       test     eax, eax
       jne      SHORT G_M_IG10
 
G_M_IG14:                ;; offset=0x0151
       mov      rdi, r15
       mov      r11, 0xNATIVE_POINTER
       call     [r11]System.IDisposable:Dispose():this
 
G_M_IG15:                ;; offset=0x0161
       nop      
 
G_M_IG16:                ;; offset=0x0162
       add      rsp, 24
       pop      rbx
       pop      r14
       pop      r15
       pop      rbp
       ret      
 
G_M_IG17:                ;; offset=0x016D
       call     CORINFO_HELP_OVERFLOW
 
G_M_IG18:                ;; offset=0x0172
       mov      edi, 23
       call     [System.ThrowHelper:ThrowArgumentNullException(int)]
       int3     
 
G_M_IG19:                ;; offset=0x017E
       push     rbp
       push     r15
       push     r14
       push     rbx
       push     rax
       mov      rbp, qword ptr [rdi]
       mov      qword ptr [rsp], rbp
       lea      rbp, [rbp+0x30]
 
G_M_IG20:                ;; offset=0x0190
       cmp      gword ptr [rbp-0x20], 0
       je       SHORT G_M_IG21
       mov      rdi, gword ptr [rbp-0x20]
       mov      r11, 0xNATIVE_POINTER
       call     [r11]System.IDisposable:Dispose():this
 
G_M_IG21:                ;; offset=0x01A8
       nop      
 
G_M_IG22:                ;; offset=0x01A9
       add      rsp, 8
       pop      rbx
       pop      r14
       pop      r15
       pop      rbp
       ret      
 
; Total bytes of code 436



; Assembly listing for method System.Collections.Generic.List`1[GameplayTags.GameplayTag]:CopyTo(GameplayTags.GameplayTag[],int):this (FullOpts)
; Emitting BLENDED_CODE for X64 with AVX512 - Unix
; FullOpts code
; optimized code
; rsp based frame
; fully interruptible
; No PGO data

G_M_IG01:                ;; offset=0x0000
       mov      r8, rdi
       mov      ecx, edx
 
G_M_IG02:                ;; offset=0x0005
       mov      rdi, gword ptr [r8+0x08]
       mov      r8d, dword ptr [r8+0x10]
       mov      rdx, rsi
       xor      esi, esi
 
G_M_IG03:                ;; offset=0x0012
       tail.jmp [System.Array:Copy(System.Array,int,System.Array,int,int)]
 
; Total bytes of code 24

