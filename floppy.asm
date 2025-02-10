	org $0800

; documentation used: https://wiki.osdev.org/Floppy_Disk_Controller

	cli

	cld

	xor ax,ax
	mov si,ax

	out $80,al

	; initialize stack
	mov ss,ax
	mov ax,#$0800
	mov sp,ax

	; set interrupt vector for floppy
	mov ax,#intvec
	mov word [6 * 4 + 0],ax
	mov ax,cs
	mov word [6 * 4 + 2],ax
	; skip over interrupt vector & data
	jmp init_continue
interrupt_triggered:
	db $00
intvec:
	push ax
	mov al,#$ff
	mov interrupt_triggered,al
	;
	mov al,#$20  ; EOI code
	out $20,al  ; send 'end of interrupt'
	pop ax
	iret

progress:
	dw $0000

unmask_floppy:
	; * PUT OCW1
	push AX
	; 8259 port 21
	mov al,#$bf
	; IMR, interrupt mask register
	; only allow irq 6
	out $21,al
	pop AX
	ret

mask_floppy:
	; * PUT OCW1
	push AX
	; 8259 port 21
	mov al,#$ff
	; IMR, interrupt mask register
	; allow no interrupts
	out $21,al
	pop AX
	ret

clear_interrupt_flag:
	push ax
	xor ax,ax
	mov interrupt_triggered,al
	pop ax
	ret

wait_interrupt_success:
	push cx
	mov cx,#0000
loop_02:
	cmp byte interrupt_triggered,#$ff
	jz int_received
	loop loop_02
	cli
	hlt
int_received:
	pop cx
	ret

wait_interrupt_none:
	push cx
	mov cx,#0000
loop_win:
	cmp byte interrupt_triggered,#$ff
	jz win_int_received2
	loop loop_win
	jp loop_win_ok
win_int_received2:
	cli
	hlt ; error!
loop_win_ok:
	pop cx
	ret

reset_floppy:
	; trigger floppy reset
	mov dx,#$3f2
	; ...enter
	mov al,#$00
	out dx,al
	; ...exit
	mov al,#$0c
	out dx,al
	ret

; *** MAIN ***
init_continue:
	mov progress,#$0001

	call unmask_floppy
	call clear_interrupt_flag

	mov progress,#$0002
	sti
	mov progress,#$0003
	call reset_floppy
	call wait_interrupt_success

	; *** all good ***
	mov progress,#$7fff
	mov ax,#$a5ee
	mov si,ax
	mov al,#$ff
	out $80,al
	cli
	hlt
