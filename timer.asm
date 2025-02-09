	org $0800

	cli

	cld

	xor ax,ax
	mov si,ax

	out $80,al

	mov ss,ax
	mov ax,#$0800
	mov sp,ax

	; setup IRQ 0
	mov ax,#int0vec
	mov word [0],ax
	mov ax,cs
	mov word [2],ax
	jmp init_continue

int0triggered:
	dw $0
int0vec:
	mov int0triggered,#$ffff
	mov al,#$20  ; EOI code
	out $20,al  ; send 'end of interrupt'
	iret

progress:
	dw $0

reset_int0_state:
	mov int0triggered,#$0000
	ret

wait_interrupt:
	mov cx, #$1000
wait_interrupt_loop:
	mov ax,int0triggered
	cmp ax,#$ffff
	jz wait_interrupt_ok
	loop wait_interrupt_loop
	hlt
wait_interrupt_ok:
	ret

wait_NO_interrupt:
	mov cx, #$1000
wait_NO_interrupt_loop:
	mov ax,int0triggered
	cmp ax,#$ffff
	jz wait_NO_interrupt_err
	loop wait_NO_interrupt_loop
	ret
wait_NO_interrupt_err:
	hlt

unmask_timer:
	; * PUT OCW1
	push AX
	; 8259 port 21
	mov al,#$fe
	; IMR, interrupt mask register
	; only allow irq 0
	out $21,al
	pop AX
	ret

init_continue:
	mov progress,#$001
	call reset_int0_state
	mov progress,#$002
	call unmask_timer
	sti

; 00: counter
; 11: L/H write
; 001: mode (one shot)
; 0: binary
	mov al,#$32
	out $43,al
; set counter to 8192
; low nibble first
	mov al,#$0
	out $40,al
	mov al,#$20
	out $40,al
; now the interrupt is counting
	mov progress,#$008
	call wait_interrupt
	mov progress,#$010
	call reset_int0_state
	call wait_NO_interrupt


	; *** finished! all good
	mov progress,#$fff
	mov ax,#$a5ee
	mov si,ax
	mov al,#$ff
	out $80,al
	cli
	hlt
