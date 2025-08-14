// do not run this shortly before midnight (local time)

// with code from https://wiki.osdev.org/Floppy_Disk_Controller
// and https://www.ardent-tool.com/floppy/Floppy_Programming.html

#include <bios.h>
#include <conio.h>
#include <i86.h>
#include <stdio.h>

char buffer[512];
unsigned segment;
unsigned offset;
long addr;
unsigned page;

enum FloppyRegisters
{
	STATUS_REGISTER_A                = 0x3F0, // read-only
	STATUS_REGISTER_B                = 0x3F1, // read-only
	DIGITAL_OUTPUT_REGISTER          = 0x3F2,
	TAPE_DRIVE_REGISTER              = 0x3F3,
	MAIN_STATUS_REGISTER             = 0x3F4, // read-only
	DATARATE_SELECT_REGISTER         = 0x3F4, // write-only
	DATA_FIFO                        = 0x3F5,
	DIGITAL_INPUT_REGISTER           = 0x3F7, // read-only
	CONFIGURATION_CONTROL_REGISTER   = 0x3F7  // write-only
};

enum FloppyCommands
{
	READ_TRACK =                 2,	// generates IRQ6
	SPECIFY =                    3,      // * set drive parameters
	SENSE_DRIVE_STATUS =         4,
	WRITE_DATA =                 5,      // * write to the disk
	READ_DATA =                  6,      // * read from the disk
	RECALIBRATE =                7,      // * seek to cylinder 0
	SENSE_INTERRUPT =            8,      // * ack IRQ6, get status of last command
	WRITE_DELETED_DATA =         9,
	READ_ID =                    10,	// generates IRQ6
	READ_DELETED_DATA =          12,
	FORMAT_TRACK =               13,     // *
	DUMPREG =                    14,
	SEEK =                       15,     // * seek both heads to cylinder X
	VERSION =                    16,	// * used during initialization, once
	SCAN_EQUAL =                 17,
	PERPENDICULAR_MODE =         18,	// * used during initialization, once, maybe
	CONFIGURE =                  19,     // * set controller parameters
	LOCK =                       20,     // * protect controller params from a reset
	VERIFY =                     22,
	SCAN_LOW_OR_EQUAL =          25,
	SCAN_HIGH_OR_EQUAL =         29
};

long get_ticks()
{
	long time_of_day;
	_bios_timeofday(_TIME_GETCLOCK, &time_of_day);
	return time_of_day;
}

volatile char received_irq = 0;
void interrupt floppy_handler()  // FIXME int 6
{
	received_irq = 1;

	outp(0x20, 0x20);  /* Non-specific EOI to master PIC */
}

void switch_on()
{
	long s;
	outp(DIGITAL_OUTPUT_REGISTER, 0x1c);  /* 0x10: motor 0 on, 0x04: set (instead of reset), 0x08: dma */

	s = get_ticks();  /* wait a second */
	while(get_ticks() - s < 18) {
	}
}

void start_dma()
{
	outp(0x08, 0x14);  // init & disable dma controller

	outp(0x0b, 0x46);  // single write transfer to main mem, channel 2

	// output the address to the DMA controller
	outp(0x0c, 255);  // reset flip-flop
	outp(0x04, addr & 255);  // low byte
	outp(0x04, addr >> 8);  // high byte

	outp(0x0c, 255);  // reset flipflop
	outp(0x05, 0xff);  // 511 (sector size - 1)
	outp(0x05, 0x01);

	outp(0x81, page);  // page register

	outp(0x0a, 0x02);  // release channel 2

	outp(0x08, 0x10);  // enable dma
}

int send_command(char cmd[], int send_n, char wait_reply, char result[], int recv_n)
{
	int i;
	long start;
	unsigned dummy;
	int msr = inp(MAIN_STATUS_REGISTER);
	if ((msr & 0xc0) != 0x80) {
		printf("floppy not ready for command\n");
		return -1;
	}

	printf("CMD: ");
	for(i=0; i<send_n; i++)
		printf("%02x ", cmd[i]);
	printf("\n");

	received_irq = 0;

	if (wait_reply == 2)
		start_dma();

	for(i=0; i<send_n; i++)
		outp(DATA_FIFO, cmd[i]);

	if (wait_reply == 0)
		return 0;

	start = get_ticks();
	while(received_irq == 0) {
		if (get_ticks() - start >= 18) {  // 1 second
			printf("no irq from floppy\n");
			return -1;
		}
	}

	if (wait_reply == 1)
		return 0;

	start = get_ticks();
	while((inp(MAIN_STATUS_REGISTER) & 0xc0) != 0xc0) {
		if (get_ticks() - start >= 18) {  // 1 second
			printf("no response from floppy\n");
			return -1;
		}
	}

	for(i=0; i<recv_n; i++) {
		if ((inp(MAIN_STATUS_REGISTER) & 0xd0) != 0xd0) {
			printf("msr invalid\n");
			return -1;
		}

		result[i] = inp(DATA_FIFO);
	}

	dummy = inp(MAIN_STATUS_REGISTER);
	if ((dummy & 0x80) != 0x80) {
		outp(0xffff, 4);
		printf("resulting msr invalid: %02x\n", dummy);
		return -1;
	}

	return 0;
}

void enter()
{
	printf("\aPress enter: ");  // beep
	getchar();
}

void main()
{
	int i;
	char cmd_read[] = { 0x06 /* read */, 0x00 /* head/unit 0 */, 0/*c*/, 0/*h*/, 1/*r*/, 2/*n*/, 9/*eot*/, 42/*gpl*/, 255/*dtl*/ };
	char cmd_specify[] = { 0x04 /*specify*/, 0xff/*SPRT/HUT: max delay*/, 254/*loadtimemax + dma */ };
	char cmd_seek[] = { 0x0f /*seek*/, 0x00/*head/unit*/, 0/*cylinder*/ };
	char result[7] = { 0 };
	typedef void interrupt (*inthandler)();
	inthandler far *ivt = (inthandler far *)0;
	inthandler oldhandler;

	printf(__DATE__ " " __TIME__ "\n");

	segment = FP_SEG(buffer);
	offset  = FP_OFF(buffer);
	addr = (long)segment * 16 + offset;
	page = addr >> 16;

	printf("buffer: %p\n", buffer);
	printf("buffer: %04x:%04x %06x %02x\n", segment, offset, addr, page);

	for(i=0; i<512; i++)
		buffer[i] = i;

	enter();

	outp(0xffff, 1);

	oldhandler = ivt[0x0e];  // Save old handler, https://wiki.osdev.org/Interrupt_Vector_Table
	ivt[0x0e] = floppy_handler;    // Install new handler

	outp(0xffff, 2);

	switch_on();

	outp(0xffff, 3);

//	printf("%d\n", send_command(cmd_specify, 3, 0, result, 0));
	printf("%d\n", send_command(cmd_seek, 3, 1, result, 7));

	printf("%d\n", send_command(cmd_read, 9, 2, result, 7));
	outp(0xffff, 4);
	printf("ST0: %02x\n", result[0]);
	printf("ST1: %02x\n", result[1]);
	printf("ST2: %02x\n", result[2]);
	printf("C  : %d\n", result[3]);
	printf("H  : %d\n", result[4]);
	printf("R  : %d\n", result[5]);
	printf("N  : %d\n", result[6]);

	for(i=0; i<128; i++)
		printf("%02x ", buffer[i]);
	printf("\n");

	ivt[0x0e] = oldhandler;    // Install new handler

	enter();
}
